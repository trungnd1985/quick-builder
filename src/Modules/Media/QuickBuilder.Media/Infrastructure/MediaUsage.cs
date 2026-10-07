using QuickBuilder.Cms.Contracts.Media;
using QuickBuilder.Pages.Contracts.Media;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Where a site's media files are used: by pages and components (013, through Pages) and by
/// collection items (015 research IT-15, through Cms), added together.
/// </summary>
/// <remarks>
/// The library, the usage panel and the delete and replace rules all read usage here, so an item
/// counts as a place like a page does: a file a published item shows is in live use (013 MV-06,
/// unchanged), and any use needs the in-use confirmation.
/// </remarks>
internal sealed class MediaUsage(IMediaUsageReader pages, IItemMediaUsageReader items)
{
    public async Task<IReadOnlyDictionary<Guid, MediaUsageSummary>> SummariseAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken)
    {
        var byPages = await pages.SummariseAsync(siteId, mediaIds, cancellationToken).ConfigureAwait(false);
        var byItems = await items.SummariseAsync(siteId, mediaIds, cancellationToken).ConfigureAwait(false);

        return mediaIds.Distinct().ToDictionary(id => id, id =>
        {
            var page = byPages.GetValueOrDefault(id) ?? MediaUsageSummary.None;
            var item = byItems.GetValueOrDefault(id) ?? ItemMediaUsageSummary.None;
            return new MediaUsageSummary(
                page.Places + item.Items,
                page.DraftPlaces + item.DraftItems,
                page.LivePlaces + item.LiveItems,
                page.HistoryOnlyPlaces + item.HistoryOnlyItems);
        });
    }

    public Task<IReadOnlyList<MediaUsagePlace>> PagePlacesAsync(Guid siteId, Guid mediaId, CancellationToken cancellationToken) =>
        pages.ListAsync(siteId, mediaId, cancellationToken);

    public Task<IReadOnlyList<ItemMediaUsagePlace>> ItemPlacesAsync(Guid siteId, Guid mediaId, CancellationToken cancellationToken) =>
        items.ListAsync(siteId, mediaId, cancellationToken);
}
