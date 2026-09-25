using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Removes stored bytes that no library entry points to (003 FR-012, research M-15).
/// </summary>
/// <remarks>
/// <para>
/// Bytes without a row come from a crash between writing and recording, a failed insert
/// whose clean-up also failed, or a failed purge after a delete. They are never visible —
/// nothing serves or lists bytes without a row — but they take space until this removes them.
/// </para>
/// <para>
/// <b>Safe by construction.</b> An upload writes its bytes and then its row within seconds,
/// so bytes younger than <c>Media:OrphanGracePeriod</c> may belong to an upload still in
/// progress and are never touched. Deleting a missing key succeeds, so two instances
/// sweeping together at worst delete the same key twice — the same no-lock approach as
/// 002's <c>IdentityRetentionService</c>.
/// </para>
/// <para>
/// <b>One pass.</b> Storage is listed once; only keys shaped like media keys are considered,
/// in batches checked against the table. Results are reported per site, which is how an
/// installation-wide job stays namespaced by site (Principle V). No file name is ever logged.
/// </para>
/// </remarks>
internal sealed class MediaOrphanSweepService(
    IServiceScopeFactory scopes,
    IFileStorage storage,
    IOptions<MediaOptions> options,
    TimeProvider clock,
    ILogger<MediaOrphanSweepService> logger) : BackgroundService
{
    internal const int BatchSize = 1_000;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Media orphan sweep failed; retrying next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs one sweep. Returns how many stored objects were removed. Exposed for tests.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - options.Value.OrphanGracePeriod;
        var removedBySite = new Dictionary<Guid, int>();
        var batch = new List<(StoredFileInfo Entry, Guid SiteId)>(BatchSize);

        await foreach (var entry in storage.ListAsync(string.Empty, cancellationToken).ConfigureAwait(false))
        {
            // Young bytes may be an upload in progress; anything not shaped like a media key
            // belongs to some other consumer of the storage.
            if (entry.LastModifiedUtc >= cutoff || MediaStorageKeys.SiteOf(entry.Key) is not { } siteId)
            {
                continue;
            }

            batch.Add((entry, siteId));
            if (batch.Count == BatchSize)
            {
                await RemoveOrphansAsync(batch, removedBySite, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }

        await RemoveOrphansAsync(batch, removedBySite, cancellationToken).ConfigureAwait(false);

        foreach (var (siteId, count) in removedBySite)
        {
            logger.LogInformation("Media orphan sweep removed {Count} stored file(s) with no library entry on site {SiteId}.", count, siteId);
        }

        return removedBySite.Values.Sum();
    }

    private async Task RemoveOrphansAsync(
        List<(StoredFileInfo Entry, Guid SiteId)> batch,
        Dictionary<Guid, int> removedBySite,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var keys = batch.Select(b => b.Entry.Key.Value).ToList();

        HashSet<string> recorded;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            recorded = (await database.MediaFiles
                    .AcrossAllSites("Background sweep compares stored objects with every site's rows (003 research M-15).")
                    .AsNoTracking()
                    .Where(f => keys.Contains(f.StorageKey))
                    .Select(f => f.StorageKey)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToHashSet(StringComparer.Ordinal);
        }

        foreach (var (entry, siteId) in batch)
        {
            if (recorded.Contains(entry.Key.Value))
            {
                continue;
            }

            await storage.DeleteAsync(entry.Key, cancellationToken).ConfigureAwait(false);
            removedBySite[siteId] = removedBySite.GetValueOrDefault(siteId) + 1;
        }
    }
}
