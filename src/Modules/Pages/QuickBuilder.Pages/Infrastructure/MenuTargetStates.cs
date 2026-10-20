using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Features.Menus;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>The states of menu targets as the admin shows them (024 FR-025, data-model §4.3).</summary>
internal static class MenuTargetStateNames
{
    public const string Live = "live";

    public const string NotPublished = "not_published";

    public const string Deleted = "deleted";

    /// <summary>A published item whose collection has no live template, so no page.</summary>
    public const string NoPage = "no_page";
}

/// <summary>
/// Whether each page or item a menu points at is live, not published, deleted, or an item
/// without a page (024 FR-025, data-model §4.3).
/// </summary>
/// <remarks>
/// A handful of queries for any number of targets: pages with their publications, items through
/// Cms, then the live templates of those items' collections. Titles are the page's current title
/// and the item's current name, for the admin; visitors see the live title and published name
/// (NV-09).
/// </remarks>
internal sealed class MenuTargetStates(PagesDbContext database, ICollectionSchemaReader collections)
{
    public async Task<IReadOnlyDictionary<(string Kind, Guid Id), MenuTargetStateResponse>> StatesAsync(
        Guid siteId, IEnumerable<(string Kind, Guid Id)> targets, CancellationToken cancellationToken)
    {
        var distinct = targets.ToHashSet();
        var states = new Dictionary<(string Kind, Guid Id), MenuTargetStateResponse>();

        var pageIds = distinct.Where(t => t.Kind == MenuTargetKinds.Page).Select(t => t.Id).ToList();
        if (pageIds.Count > 0)
        {
            var pages = await database.ForSite<Page>(siteId)
                .AsNoTracking()
                .Where(p => pageIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    p.Title,
                    // 016 TB-02: a template page has no address of its own, so it is never live as a target.
                    IsLive = p.TemplateCollectionId == null
                             && database.ForSite<Publication>(siteId).Any(x => x.SubjectKind == SubjectKinds.Page && x.SubjectId == p.Id && x.LiveVersionId != null),
                })
                .ToDictionaryAsync(p => p.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (var id in pageIds)
            {
                states[(MenuTargetKinds.Page, id)] = pages.TryGetValue(id, out var page)
                    ? new(MenuTargetKinds.Page, id, page.IsLive ? MenuTargetStateNames.Live : MenuTargetStateNames.NotPublished, page.Title)
                    : new(MenuTargetKinds.Page, id, MenuTargetStateNames.Deleted, null);
            }
        }

        var itemIds = distinct.Where(t => t.Kind == MenuTargetKinds.Item).Select(t => t.Id).ToList();
        if (itemIds.Count > 0)
        {
            var items = await collections.GetItemAddressPartsAsync(siteId, itemIds, cancellationToken).ConfigureAwait(false);
            var collectionIds = items.Values.Select(i => i.CollectionId).Distinct().ToList();
            var withLiveTemplate = (await database.ForSite<Page>(siteId)
                    .AsNoTracking()
                    .Where(p => p.TemplateCollectionId != null && collectionIds.Contains(p.TemplateCollectionId.Value)
                                && database.ForSite<Publication>(siteId).Any(x => x.SubjectKind == SubjectKinds.Page && x.SubjectId == p.Id && x.LiveVersionId != null))
                    .Select(p => p.TemplateCollectionId!.Value)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToHashSet();

            foreach (var id in itemIds)
            {
                states[(MenuTargetKinds.Item, id)] = items.TryGetValue(id, out var item)
                    ? new(MenuTargetKinds.Item, id, ItemState(item, withLiveTemplate), item.Name)
                    : new(MenuTargetKinds.Item, id, MenuTargetStateNames.Deleted, null);
            }
        }

        return states;
    }

    private static string ItemState(ItemAddressParts item, HashSet<Guid> withLiveTemplate) =>
        item.PublishedSlug is null ? MenuTargetStateNames.NotPublished
        : withLiveTemplate.Contains(item.CollectionId) ? MenuTargetStateNames.Live
        : MenuTargetStateNames.NoPage;
}
