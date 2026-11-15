using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.Media.Variants;

/// <summary>
/// Produces thumbnails right after upload and replacement (013 clarification, research MV-13).
/// </summary>
/// <remarks>
/// Reads <see cref="ThumbnailQueue"/> and asks <see cref="VariantService"/> for the smallest
/// configured width, so a thumbnail is produced exactly as any other variant is: once, under
/// the claim, the slot limit and the timeout. A request that is no longer current (replaced,
/// deleted, or without variants) is skipped. Nothing is lost for good if this falls behind:
/// the first request for a thumbnail produces it.
/// </remarks>
internal sealed class ThumbnailWorker(
    ThumbnailQueue queue,
    IServiceScopeFactory scopes,
    VariantService variants,
    ImagingRuntime imaging,
    IOptions<MediaOptions> options,
    ILogger<ThumbnailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await ProduceAsync(request, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex,
                        "The thumbnail of media file {MediaFileId} on site {SiteId} was not produced; it will be on first request.",
                        request.FileId, request.SiteId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Produces one thumbnail if it is still wanted. Exposed for tests.</summary>
    internal async Task ProduceAsync(ThumbnailRequest request, CancellationToken cancellationToken)
    {
        MediaFile? file;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            file = await database.MediaFiles
                .AcrossAllSites("A background thumbnail for a file named by its site and id (013 MV-13).")
                .IncludingTrash("A file trashed while its thumbnail was queued still gets it, for its restore (021 TR-18).")
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.FileId && f.SiteId == request.SiteId, cancellationToken)
                .ConfigureAwait(false);
        }

        var width = options.Value.Variants.ThumbnailWidth;
        if (file is null
            || file.ContentVersion != request.ContentVersion
            || !VariantPlan.Allows(file, width, options.Value.Variants.Widths, imaging.AvifVariants))
        {
            return;
        }

        var result = await variants.GetAsync(file, width, cancellationToken).ConfigureAwait(false);
        if (result is VariantResult.Stored stored)
        {
            await stored.File.DisposeAsync().ConfigureAwait(false);
        }
    }
}
