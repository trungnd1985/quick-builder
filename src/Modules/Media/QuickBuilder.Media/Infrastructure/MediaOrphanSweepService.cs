using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Domain;
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

    /// <summary>How old a variant must be before the sweep may remove it (013 MV-14): productions racing a replace finish first.</summary>
    internal static readonly TimeSpan VariantGracePeriod = TimeSpan.FromHours(1);

    /// <summary>Runs one sweep. Returns how many stored objects were removed. Exposed for tests.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - options.Value.OrphanGracePeriod;
        var variantCutoff = now - VariantGracePeriod;
        var removedBySite = new Dictionary<Guid, int>();
        var variantsBySite = new Dictionary<Guid, int>();
        var batch = new List<(StoredFileInfo Entry, Guid SiteId)>(BatchSize);
        var variantBatch = new List<(StorageKey Key, VariantKey Variant)>(BatchSize);

        await foreach (var entry in storage.ListAsync(string.Empty, cancellationToken).ConfigureAwait(false))
        {
            // 013 MV-14: variants have their own prefix and their own rule.
            if (MediaStorageKeys.ParseVariant(entry.Key) is { } variant)
            {
                if (entry.LastModifiedUtc < variantCutoff)
                {
                    variantBatch.Add((entry.Key, variant));
                    if (variantBatch.Count == BatchSize)
                    {
                        await RemoveStaleVariantsAsync(variantBatch, variantsBySite, cancellationToken).ConfigureAwait(false);
                        variantBatch.Clear();
                    }
                }

                continue;
            }

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
        await RemoveStaleVariantsAsync(variantBatch, variantsBySite, cancellationToken).ConfigureAwait(false);

        foreach (var (siteId, count) in removedBySite)
        {
            logger.LogInformation("Media orphan sweep removed {Count} stored file(s) with no library entry on site {SiteId}.", count, siteId);
        }

        foreach (var (siteId, count) in variantsBySite)
        {
            logger.LogInformation("Media orphan sweep removed {Count} stale variant(s) on site {SiteId}.", count, siteId);
        }

        return removedBySite.Values.Sum() + variantsBySite.Values.Sum();
    }

    /// <summary>
    /// Removes variants nobody can request any more (013 MV-14): the file is gone, its version
    /// has moved on, the width is no longer configured, or its variants were withdrawn.
    /// </summary>
    private async Task RemoveStaleVariantsAsync(
        List<(StorageKey Key, VariantKey Variant)> batch,
        Dictionary<Guid, int> removedBySite,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var ids = batch.Select(b => b.Variant.FileId).Distinct().ToList();
        Dictionary<Guid, (Guid SiteId, int Version, MediaVariantsState State)> current;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            current = await database.MediaFiles
                .AcrossAllSites("Background sweep compares stored variants with every site's rows (013 MV-14).")
                .AsNoTracking()
                .Where(f => ids.Contains(f.Id))
                .Select(f => new { f.Id, f.SiteId, f.ContentVersion, f.VariantsState })
                .ToDictionaryAsync(f => f.Id, f => (f.SiteId, f.ContentVersion, f.VariantsState), cancellationToken)
                .ConfigureAwait(false);
        }

        var widths = options.Value.Variants.Widths;
        foreach (var (key, variant) in batch)
        {
            var stillServed = current.TryGetValue(variant.FileId, out var file)
                && file.SiteId == variant.SiteId
                && file.Version == variant.Version
                && file.State == MediaVariantsState.Available
                && widths.Contains(variant.Width);

            if (stillServed)
            {
                continue;
            }

            await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            removedBySite[variant.SiteId] = removedBySite.GetValueOrDefault(variant.SiteId) + 1;
        }
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
