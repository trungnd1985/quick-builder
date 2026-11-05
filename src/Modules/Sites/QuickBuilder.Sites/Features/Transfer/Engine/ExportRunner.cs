using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Versioning;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// Exports one site to an archive in storage (020 FR-001 … FR-009, research ST-04, ST-16).
/// </summary>
/// <remarks>
/// Every part reads inside one <c>REPEATABLE READ, READ ONLY</c> snapshot, so the archive is
/// one point in time while members keep editing (FR-005, FR-006). The ZIP is piped straight
/// into storage, which keeps memory bounded and leaves no object behind if the export fails:
/// storage writes are all or nothing.
/// </remarks>
internal sealed class ExportRunner(
    IServiceScopeFactory scopes,
    IEnumerable<ISiteArchivePart> parts,
    ISiteContextSetter siteContext,
    ISiteContext currentSite,
    ISiteDirectory sites,
    IUserDirectory users,
    IFileStorage storage,
    IConfiguration configuration,
    ApplicationVersion version,
    TimeProvider clock,
    ILogger<ExportRunner> logger) : ITransferRunner
{
    public bool CanRun(TransferJob job) => job.Kind == TransferKinds.Export && job.State == TransferStates.Running;

    public async Task RunAsync(TransferJob job, TransferLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(lease);

        var siteId = job.SiteId ?? throw new InvalidOperationException("An export names its site.");
        var outcome = await ExportAsync(job.Id, siteId, lease, cancellationToken).ConfigureAwait(false);

        await lease.DisposeAsync().ConfigureAwait(false);
        await FinishAsync(job.Id, outcome, lease.CancelRequested, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the archive of <paramref name="siteId"/> under the job's key and returns its size
    /// and summary, or the failure. Also used by a restore for its pre-restore export.
    /// </summary>
    internal async Task<ExportOutcome> ExportAsync(Guid jobId, Guid siteId, TransferLease lease, CancellationToken cancellationToken)
    {
        // The job's scope reads this site through the module contexts' filters (analysis U2). A
        // restore's pre-restore export runs in a scope already set to the same site.
        if (currentSite.SiteId != siteId)
        {
            siteContext.SetSite(siteId);
        }

        var site = await sites.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return ExportOutcome.Failed(TransferFailures.Unexpected);
        }

        var key = TransferStorageKeys.ForSite(siteId, jobId);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var problems = new List<ArchiveProblem>();
        var authors = new AuthorCollector();
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 4 * 1024 * 1024, resumeWriterThreshold: 2 * 1024 * 1024));
        CountingStream? counted = null;

        // Storage reads the pipe while the parts fill it.
        var stored = Task.Run(async () =>
        {
            await using var content = pipe.Reader.AsStream();
            counted = new CountingStream(content);
            await storage.WriteAsync(key, counted, cancellationToken).ConfigureAwait(false);
        }, CancellationToken.None);

        try
        {
            await using (var session = await SiteTransferSession.BeginSnapshotAsync(ConnectionString(), cancellationToken).ConfigureAwait(false))
            {
                await using var output = pipe.Writer.AsStream();
                await using var writer = await ZipArchiveWriter.CreateAsync(output, cancellationToken).ConfigureAwait(false);

                var ordered = parts.OrderBy(p => p.Order).ToList();
                foreach (var part in ordered)
                {
                    lease.Step(part.Name);
                    var context = new PartExportContext(
                        siteId,
                        session,
                        writer,
                        authors,
                        lease,
                        (kind, n) => counts[kind] = counts.GetValueOrDefault(kind) + n,
                        problems.Add);
                    await part.ExportAsync(context, cancellationToken).ConfigureAwait(false);
                    lease.Report(1);
                }

                await writer.WriteAuthorsAsync(AuthorsAsync(authors, cancellationToken), cancellationToken).ConfigureAwait(false);
                await writer.CompleteAsync(entries => new ArchiveManifest
                {
                    Format = ArchiveFormat.CurrentFormat,
                    Release = version.Version.ToString(),
                    CreatedAtUtc = clock.GetUtcNow(),
                    Source = new ArchiveSource(site.Id, site.Name, site.Status == "enabled"),
                    Parts = [.. ordered.Select(p => new ArchivePartInfo(p.Name, p.Version, p.Contracts))],
                    Counts = counts,
                    Problems = problems,
                    Entries = entries,
                }, cancellationToken).ConfigureAwait(false);
            }

            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            await stored.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ends storage's read with the failure, so it keeps nothing of the archive.
            await pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
            try
            {
                await stored.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The write failed as intended; the original failure is what matters.
            }

            if (ex is OperationCanceledException)
            {
                return ExportOutcome.Cancelled();
            }

            logger.LogError(ex, "Exporting site {SiteId} (transfer {TransferId}) failed.", siteId, jobId);
            return ExportOutcome.Failed(ex is IOException ? TransferFailures.StorageFailed : TransferFailures.Unexpected);
        }

        var bytes = counted?.Count ?? 0;
        var summary = JsonSerializer.Serialize(new { counts, archiveBytes = bytes, problems }, ArchiveJson.Options);
        logger.LogInformation("Exported site {SiteId} (transfer {TransferId}): {Bytes} bytes.", siteId, jobId, bytes);
        return ExportOutcome.Succeeded(key.Value, bytes, summary);
    }

    private async IAsyncEnumerable<ArchiveAuthor> AuthorsAsync(AuthorCollector authors, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var batch in authors.Ids.Chunk(500))
        {
            foreach (var user in await users.FindManyAsync(batch, cancellationToken).ConfigureAwait(false))
            {
                yield return new ArchiveAuthor(user.Id, user.DisplayName);
            }
        }
    }

    private async Task FinishAsync(Guid jobId, ExportOutcome outcome, bool cancelRequested, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
        var job = await store.FindAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Transfer {jobId} disappeared while it ran.");
        var now = clock.GetUtcNow();

        if (outcome.ArchiveKey is { } key)
        {
            job.ExportSucceeded(key, outcome.Bytes, outcome.Summary!, now);
        }
        else if (outcome.IsCancelled || cancelRequested)
        {
            job.Cancelled(now);
        }
        else
        {
            job.Failed(outcome.FailureCode!, now);
        }

        await store.SaveAsync(job, [TransferOutcomes.FinishedEvent(job, now)], cancellationToken).ConfigureAwait(false);
    }

    private string ConnectionString() =>
        configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");

    /// <summary>A pass-through stream that counts the bytes read through it.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Count { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => Count; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Count += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Count += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>The result of writing an archive.</summary>
internal sealed record ExportOutcome(string? ArchiveKey, long Bytes, string? Summary, string? FailureCode, bool IsCancelled)
{
    public static ExportOutcome Succeeded(string key, long bytes, string summary) => new(key, bytes, summary, null, false);

    public static ExportOutcome Failed(string code) => new(null, 0, null, code, false);

    public static ExportOutcome Cancelled() => new(null, 0, null, null, true);
}

/// <summary>Collects every member id the parts' rows name (research ST-14).</summary>
internal sealed class AuthorCollector : IAuthorCollector
{
    private readonly HashSet<Guid> _ids = [];

    public IReadOnlyCollection<Guid> Ids => _ids;

    public void Add(Guid? userId)
    {
        if (userId is { } id && id != Guid.Empty)
        {
            _ids.Add(id);
        }
    }
}

/// <summary>Where archives live in storage (research ST-16).</summary>
internal static class TransferStorageKeys
{
    public const string Segment = "transfers";

    /// <summary>An export of a site, or an upload into an existing one.</summary>
    public static StorageKey ForSite(Guid siteId, Guid jobId) => new($"{siteId:N}/{Segment}/{jobId:N}");

    /// <summary>An upload made before any site exists (import as a new site).</summary>
    public static StorageKey ForInstallation(Guid jobId) => new($"installation/{Segment}/{jobId:N}");
}
