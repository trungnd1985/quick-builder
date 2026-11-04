using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Versioning;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// Checks an uploaded archive against its target before anything changes (020 FR-011 … FR-016,
/// research ST-06).
/// </summary>
/// <remarks>
/// In order: the container's shape and every entry's hash, the archive's format and release,
/// then each part's own check, which validates its entries, declares and matches its ids, and
/// lists conflicts, removals and warnings. The result and the id map's fingerprint are stored on
/// the job, which then waits for confirmation, or is refused when there is any error.
/// </remarks>
internal sealed class CheckRunner(
    IServiceScopeFactory scopes,
    IEnumerable<ISiteArchivePart> parts,
    ISiteContextSetter siteContext,
    ISiteDirectory sites,
    IFileStorage storage,
    IConfiguration configuration,
    ApplicationVersion version,
    TimeProvider clock,
    ILogger<CheckRunner> logger) : ITransferRunner
{
    public bool CanRun(TransferJob job) => job.Kind == TransferKinds.Import && job.State == TransferStates.Checking;

    public async Task RunAsync(TransferJob job, TransferLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(lease);

        var mode = job.ImportMode ?? throw new InvalidOperationException("A check names its import mode.");
        if (job.SiteId is { } target)
        {
            siteContext.SetSite(target);
        }

        var outcome = await CheckAsync(job, mode, lease, cancellationToken).ConfigureAwait(false);
        await lease.DisposeAsync().ConfigureAwait(false);

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
        var fresh = await store.FindAsync(job.Id, CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Transfer {job.Id} disappeared while it was checked.");
        var now = clock.GetUtcNow();

        if (lease.CancelRequested)
        {
            fresh.Cancelled(now);
            await store.SaveAsync(fresh, [TransferOutcomes.FinishedEvent(fresh, now)], CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (outcome.Manifest is null)
        {
            fresh.RefusedUnreadable(outcome.Result, now);
        }
        else
        {
            fresh.Checked(outcome.Manifest, outcome.Result, outcome.Fingerprint!, outcome.Refused, now);
        }

        await store.SaveAsync(fresh, outcome.Refused ? [TransferOutcomes.FinishedEvent(fresh, now)] : null, CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Checked transfer {TransferId}: {Outcome}.", job.Id, outcome.Refused ? "refused" : "waiting for confirmation");
    }

    private async Task<CheckOutcome> CheckAsync(TransferJob job, ImportMode mode, TransferLease lease, CancellationToken cancellationToken)
    {
        var report = new CheckReport();
        lease.Step(TransferSteps.Reading);

        var opened = await OpenAsync(job, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            report.Error(opened.Error!.Code, subject: null, opened.Error.Message);
            return CheckOutcome.Unreadable(report.ToJson(null, false, []));
        }

        await using var archive = opened.Value;
        var verified = await archive.VerifyAllAsync(lease.Report, cancellationToken).ConfigureAwait(false);
        if (verified.IsFailure)
        {
            report.Error(verified.Error!.Code, subject: null, verified.Error.Message);
            return CheckOutcome.Unreadable(report.ToJson(archive.Manifest, false, []));
        }

        var manifest = archive.Manifest;
        CheckVersions(manifest, report);

        var sameSite = job.SiteId is { } target && manifest.Source.SiteId == target;
        var sourceHere = await sites.FindByIdAsync(manifest.Source.SiteId, cancellationToken).ConfigureAwait(false) is not null;
        string[] allowed = sourceHere ? [TransferModes.NewSite, TransferModes.Merge, TransferModes.Restore] : [TransferModes.NewSite, TransferModes.Merge];

        if (mode == ImportMode.Restore && !sameSite)
        {
            report.Error(TransferErrors.RestoreNotAllowed.Code, subject: null, TransferErrors.RestoreNotAllowed.Message);
        }

        // Parts check only an archive whose container and versions are readable at all.
        var ids = new IdMap(mode, policy: null);
        if (!report.HasErrors)
        {
            await using var session = job.SiteId is null ? null : await SiteTransferSession.BeginSnapshotAsync(ConnectionString(), cancellationToken).ConfigureAwait(false);
            var context = new PartCheckContext(archive, mode, job.SiteId, session, ids, report, lease, clock.GetUtcNow());
            foreach (var part in parts.OrderBy(p => p.Order))
            {
                lease.Step(part.Name);
                try
                {
                    await part.CheckAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (ArchiveFormatException ex)
                {
                    report.Error("transfer.document_invalid", subject: null, ex.Message);
                }
                catch (ArchiveIntegrityException ex)
                {
                    report.Error("archive.integrity", subject: null, ex.Message);
                }

                lease.Report(1);
            }
        }

        return new CheckOutcome(
            JsonSerializer.Serialize(manifest, ArchiveJson.Options),
            report.ToJson(manifest, sameSite, allowed),
            ids.Fingerprint(),
            report.HasErrors);
    }

    /// <summary>Format and release (research ST-21). Document versions are each part's to check.</summary>
    private void CheckVersions(ArchiveManifest manifest, CheckReport report)
    {
        if (!ArchiveFormat.SupportedFormats.Contains(manifest.Format))
        {
            report.Error(
                manifest.Format > ArchiveFormat.CurrentFormat ? "transfer.format_too_new" : "transfer.format_unsupported",
                subject: null,
                $"The archive is in format {manifest.Format}; this release reads format {ArchiveFormat.CurrentFormat}. Upgrade this installation first.");
            return;
        }

        if (!SemanticVersion.TryParse(manifest.Release, out var release))
        {
            report.Error("archive.unreadable", subject: null, "The archive does not say which release wrote it.");
            return;
        }

        if (release.Major > version.Version.Major)
        {
            report.Error("transfer.release_too_new", subject: null,
                $"The archive was written by release {release}; this installation is {version.Version}. Upgrade this installation first.");
        }
        else if (!ArchiveFormat.IsSupportedRelease(release, version.Version))
        {
            report.Error("transfer.release_unsupported", subject: null,
                $"Archives from release {release} cannot be read by release {version.Version}.");
        }

        foreach (var part in manifest.Parts)
        {
            foreach (var (contract, archiveVersion) in part.Contracts)
            {
                var current = parts.FirstOrDefault(p => p.Name == part.Name)?.Contracts.GetValueOrDefault(contract);
                if (current is { } known && archiveVersion > known)
                {
                    report.Error("transfer.document_too_new", subject: null,
                        $"The archive's {contract} documents are version {archiveVersion}; this installation reads up to {known}. Upgrade this installation first.");
                }
                else if (current is { } older && archiveVersion < older)
                {
                    report.Upgrade(contract, archiveVersion, older);
                }
            }
        }
    }

    /// <summary>Opens the job's stored archive for reading; two streams, one per reader channel.</summary>
    internal async Task<Result<ZipArchiveReader>> OpenAsync(TransferJob job, CancellationToken cancellationToken)
    {
        var key = new StorageKey(job.ArchiveKey ?? throw new InvalidOperationException("The transfer has no archive."));
        return await ZipArchiveReader.OpenAsync(async ct =>
        {
            var stored = await storage.OpenReadAsync(key, ct).ConfigureAwait(false)
                ?? throw new IOException("The uploaded archive is gone from storage.");
            return stored.Content;
        }, cancellationToken).ConfigureAwait(false);
    }

    private string ConnectionString() =>
        configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");

    private sealed record CheckOutcome(string? Manifest, string Result, string? Fingerprint, bool Refused)
    {
        public static CheckOutcome Unreadable(string result) => new(null, result, null, true);
    }
}
