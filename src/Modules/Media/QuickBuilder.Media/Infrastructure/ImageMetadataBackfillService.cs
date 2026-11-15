using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Records the dimensions of images uploaded before 013, and queues their thumbnails
/// (013 FR-027, research MV-10).
/// </summary>
/// <remarks>
/// <para>
/// <b>One site at a time</b> (Principle V: background jobs are namespaced by site). The only
/// cross-site read lists the sites with pending images; each is then processed in batches of
/// <see cref="BatchSize"/>, filtered to that site explicitly.
/// </para>
/// <para>
/// Each image's header is read, never its pixels. Recorded dimensions publish
/// <c>Media.FileChanged(dimensions)</c> in the same save, so live pages start stating width
/// and height and offering variants at their next request. Images over the pixel limit, and
/// headers libvips cannot read, are marked and keep being served as uploaded.
/// </para>
/// <para>
/// Runs after <c>ApplicationStarted</c>, then hourly (rows seeded or restored meanwhile are
/// picked up). Idempotent: two instances read the same header and write the same values; the
/// row version turns the slower save into a no-op.
/// </para>
/// </remarks>
internal sealed class ImageMetadataBackfillService(
    IServiceScopeFactory scopes,
    IFileStorage storage,
    ThumbnailQueue thumbnails,
    ImagingRuntime imaging,
    IOptions<MediaOptions> options,
    IHostApplicationLifetime lifetime,
    TimeProvider clock,
    ILogger<ImageMetadataBackfillService> logger) : BackgroundService
{
    internal const int BatchSize = 50;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The media image metadata backfill failed; retrying next interval.");
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

    /// <summary>Processes every pending image, site by site. Returns how many rows changed. Exposed for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        // Constructing the runtime applied the loader restrictions.
        _ = imaging;

        List<Guid> sites;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            sites = await database.MediaFiles
                .AcrossAllSites("Finds which sites still have images without dimensions; each is then processed on its own (013 MV-10).")
                .IncludingTrash("Trashed images are backfilled too, so they come back complete (021 TR-18).")
                .AsNoTracking()
                .Where(f => f.MetadataState == MediaMetadataState.Pending)
                .Select(f => f.SiteId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var total = 0;
        foreach (var siteId in sites)
        {
            var changed = await RunForSiteAsync(siteId, cancellationToken).ConfigureAwait(false);
            if (changed > 0)
            {
                logger.LogInformation("Media image metadata recorded for {Count} file(s) on site {SiteId}.", changed, siteId);
            }

            total += changed;
        }

        return total;
    }

    private async Task<int> RunForSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var changed = 0;
        var seen = new HashSet<Guid>();

        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

            var batch = await database.MediaFiles
                .AcrossAllSites("Background backfill of one site, named explicitly below (013 MV-10).")
                .IncludingTrash("Trashed images are backfilled too, so they come back complete (021 TR-18).")
                .Where(f => f.SiteId == siteId && f.MetadataState == MediaMetadataState.Pending && !seen.Contains(f.Id))
                .OrderBy(f => f.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                return changed;
            }

            var recorded = new List<MediaFile>();
            foreach (var file in batch)
            {
                seen.Add(file.Id);
                if (await ProbeAsync(file, cancellationToken).ConfigureAwait(false))
                {
                    recorded.Add(file);
                    database.OutboxMessages.Add(OutboxMessage.From(new FileChanged(
                        Guid.CreateVersion7(), clock.GetUtcNow(), file.SiteId, file.Id, FileChangeReasons.Dimensions)));
                }
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                changed += batch.Count;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance, or an edit, got there first; its values stand.
                continue;
            }

            foreach (var file in recorded)
            {
                thumbnails.Enqueue(file);
            }
        }
    }

    /// <summary>Reads one image's header and applies it. True when dimensions were recorded.</summary>
    private async Task<bool> ProbeAsync(MediaFile file, CancellationToken cancellationToken)
    {
        var stored = await storage.OpenReadAsync(new StorageKey(file.StorageKey), cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            // The bytes are gone (003's "unavailable"): nothing to read, ever.
            file.MarkMetadata(MediaMetadataState.Unreadable);
            return false;
        }

        ProbedImage probed;
        await using (stored.ConfigureAwait(false))
        {
            var maxPixels = options.Value.MaxImagePixels;
            probed = await Task.Run(() => ImageMetadataReader.Read(stored.Content, maxPixels), cancellationToken).ConfigureAwait(false);
        }

        if (probed.Metadata is { } metadata)
        {
            return file.RecordImageMetadata(metadata);
        }

        file.MarkMetadata(probed.State == MediaMetadataState.OverLimit ? MediaMetadataState.OverLimit : MediaMetadataState.Unreadable);
        return false;
    }

    private async Task<bool> StartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));

        try
        {
            await started.Task.ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
