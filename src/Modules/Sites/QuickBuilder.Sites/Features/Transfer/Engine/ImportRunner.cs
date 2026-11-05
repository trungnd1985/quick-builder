using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// Applies a confirmed import, all or nothing (020 FR-017 … FR-021a, FR-018; research ST-05).
/// </summary>
/// <remarks>
/// Every part writes into one transaction, committed once after the last part. Before that, the
/// parts' checks run again inside the transaction to rebuild the id map, and its fingerprint must
/// equal the confirmed check's (ST-06): if the target changed in between, nothing is written. A
/// failure, a cancel or a crash leaves the target as it was; media bytes written before a failed
/// commit have no row, and the media orphan sweep removes them.
/// </remarks>
internal sealed class ImportRunner(
    IServiceScopeFactory scopes,
    IEnumerable<ISiteArchivePart> parts,
    ISiteContextSetter siteContext,
    CheckRunner checks,
    IHistoricalAuthors historicalAuthors,
    NewSiteOptions newSite,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<ImportRunner> logger) : ITransferRunner
{
    public bool CanRun(TransferJob job) => job.Kind == TransferKinds.Import && job.State == TransferStates.Running;

    public async Task RunAsync(TransferJob job, TransferLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(lease);

        ImportOutcome outcome;
        try
        {
            outcome = await ImportAsync(job, lease, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lease.CancelRequested)
        {
            outcome = ImportOutcome.Cancelled();
        }
        catch (ArchiveIntegrityException ex)
        {
            logger.LogWarning(ex, "Import {TransferId} stopped: the archive changed since it was checked.", job.Id);
            outcome = ImportOutcome.Failed("archive.integrity");
        }

        await lease.DisposeAsync().ConfigureAwait(false);

        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
        var fresh = await store.FindAsync(job.Id, CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Transfer {job.Id} disappeared while it ran.");
        var now = clock.GetUtcNow();

        if (outcome.SiteId is { } siteId)
        {
            fresh.ImportSucceeded(siteId, outcome.Report!, now);
        }
        else if (outcome.IsCancelled)
        {
            fresh.Cancelled(now);
        }
        else
        {
            fresh.Failed(outcome.FailureCode!, now);
        }

        await store.SaveAsync(fresh, [TransferOutcomes.FinishedEvent(fresh, now)], CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<ImportOutcome> ImportAsync(TransferJob job, TransferLease lease, CancellationToken cancellationToken)
    {
        var mode = job.ImportMode ?? throw new InvalidOperationException("An import names its mode.");
        var targetSiteId = job.SiteId ?? Guid.CreateVersion7();
        siteContext.SetSite(targetSiteId);
        newSite.Name = job.NewSiteName;
        newSite.HostName = job.NewSiteHostName;

        lease.Step(TransferSteps.Reading);
        var opened = await checks.OpenAsync(job, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return ImportOutcome.Failed(opened.Error!.Code);
        }

        await using var archive = opened.Value;
        var ordered = parts.OrderBy(p => p.Order).ToList();

        // Authors first, in Identity's own transaction: they are names, reused by the next
        // import, so a failure below leaves nothing that matters (research ST-14).
        var named = new List<ArchivedAuthor>();
        await foreach (var author in archive.ReadAuthorsAsync(cancellationToken).ConfigureAwait(false))
        {
            named.Add(new ArchivedAuthor(author.Id, author.DisplayName));
        }

        var authors = new AuthorMap(await historicalAuthors.EnsureAsync(named, cancellationToken).ConfigureAwait(false));

        // Restore: the site as it is now is exported first, so the restore itself can be undone
        // (FR-027d). If that export fails, nothing is changed.
        var preRestore = job.PreRestoreJobId;
        if (mode == ImportMode.Restore && preRestore is null)
        {
            lease.Step(TransferSteps.PreRestore);
            preRestore = await PreRestoreExportAsync(job, targetSiteId, lease, cancellationToken).ConfigureAwait(false);
            if (preRestore is null)
            {
                return ImportOutcome.Failed(TransferFailures.PreRestoreExportFailed);
            }
        }

        await using var session = await SiteTransferSession.BeginWriteAsync(ConnectionString(), cancellationToken).ConfigureAwait(false);

        // An existing site's content writes pause until this transaction ends; writes already
        // under way finish first (FR-028, research ST-07).
        if (mode != ImportMode.NewSite)
        {
            await QuickBuilder.Shared.Infrastructure.Data.SiteWriteLock.EnterExclusiveAsync(session.Connection, session.Transaction, targetSiteId, cancellationToken).ConfigureAwait(false);
        }

        // The id map again, against the target as it is now (ST-06).
        var ids = new IdMap(mode, job.Policy);
        var recheck = new CheckReport();
        var checkContext = new PartCheckContext(archive, mode, job.SiteId, session, ids, recheck, lease, clock.GetUtcNow());
        foreach (var part in ordered)
        {
            await part.CheckAsync(checkContext, cancellationToken).ConfigureAwait(false);
        }

        if (recheck.HasErrors || ids.Fingerprint() != job.CheckFingerprint)
        {
            logger.LogWarning("Import {TransferId} stopped: the target changed since the check.", job.Id);
            return ImportOutcome.Failed(TransferFailures.TargetChanged);
        }

        var report = new ImportReport();
        var context = new PartApplyContext(
            archive, mode, targetSiteId, session, ids, authors, report, lease, job.Policy, job.StartFresh, job.StartedBy, clock.GetUtcNow());
        foreach (var part in ordered)
        {
            lease.Step(part.Name);
            await part.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
            lease.Report(1);
        }

        lease.Step(TransferSteps.Committing);
        cancellationToken.ThrowIfCancellationRequested();
        await session.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Imported transfer {TransferId} into site {SiteId} ({Mode}).", job.Id, targetSiteId, job.Mode);
        return ImportOutcome.Succeeded(
            targetSiteId,
            report.ToJson(job.Mode!, job.StartFresh, job.ConflictPolicy, targetSiteId, preRestore, authors.Count - named.Count(a => authors.Map(a.SourceUserId) == a.SourceUserId)));
    }

    /// <summary>Exports the target as it is now, as an admin export kept for 7 days, and links it to the restore.</summary>
    private async Task<Guid?> PreRestoreExportAsync(TransferJob job, Guid siteId, TransferLease lease, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var export = TransferJob.StartExport(siteId, TransferTriggers.PreRestore, job.StartedBy, now);
        export.Claim(TransferWorker.InstanceId, now);

        await using (var scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
            await store.SaveAsync(export, null, cancellationToken).ConfigureAwait(false);
            var restore = await store.FindAsync(job.Id, cancellationToken).ConfigureAwait(false);
            restore!.PreRestoreStarted(export.Id, clock.GetUtcNow());
            await store.SaveAsync(restore, null, cancellationToken).ConfigureAwait(false);
        }

        ExportOutcome outcome;
        await using (var scope = scopes.CreateAsyncScope())
        {
            outcome = await scope.ServiceProvider.GetRequiredService<ExportRunner>().ExportAsync(export.Id, siteId, lease, cancellationToken).ConfigureAwait(false);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
            var saved = await store.FindAsync(export.Id, CancellationToken.None).ConfigureAwait(false);
            var at = clock.GetUtcNow();
            if (outcome.ArchiveKey is { } key)
            {
                saved!.ExportSucceeded(key, outcome.Bytes, outcome.Summary!, at);
            }
            else
            {
                saved!.Failed(outcome.FailureCode ?? TransferFailures.Unexpected, at);
            }

            await store.SaveAsync(saved, [TransferOutcomes.FinishedEvent(saved, at)], CancellationToken.None).ConfigureAwait(false);
        }

        return outcome.ArchiveKey is null ? null : export.Id;
    }

    private string ConnectionString() =>
        configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");

    private sealed record ImportOutcome(Guid? SiteId, string? Report, string? FailureCode, bool IsCancelled)
    {
        public static ImportOutcome Succeeded(Guid siteId, string report) => new(siteId, report, null, false);

        public static ImportOutcome Failed(string code) => new(null, null, code, false);

        public static ImportOutcome Cancelled() => new(null, null, null, true);
    }
}
