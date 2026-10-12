using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features.SetPageTemplate;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// What a change to the page tree or to publishing does to item addresses (016 research TB-12,
/// data-model §4): warnings in the response, never refusals.
/// </summary>
/// <remarks>
/// Counts come from Cms through the admin-side reader, which names the site. Only ids and
/// counts are returned: an item's slug or title is content and stays in Cms.
/// </remarks>
internal sealed class AddressWarnings(PagesDbContext database, ICollectionSchemaReader items)
{
    /// <summary>
    /// <c>items_change_address</c>: the published items of every template among
    /// <paramref name="pageIds"/> and their descendants, whose addresses just changed.
    /// </summary>
    public async Task<IReadOnlyList<TemplateWarningResponse>> AddressesChangedAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken)
    {
        var templates = await TemplatesUnderAsync(siteId, pageIds, cancellationToken).ConfigureAwait(false);
        var count = 0;
        foreach (var collectionId in templates)
        {
            count += await items.CountPublishedItemsAsync(siteId, collectionId, cancellationToken).ConfigureAwait(false);
        }

        return count > 0 ? [new TemplateWarningResponse("items_change_address", count)] : [];
    }

    /// <summary><c>items_lose_address</c>: a template taken off the site leaves its items without pages.</summary>
    public async Task<IReadOnlyList<TemplateWarningResponse>> AddressesLostAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken)
    {
        var collectionId = await database.ForSite<Page>(siteId)
            .Where(p => p.Id == pageId)
            .Select(p => p.TemplateCollectionId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (collectionId is not { } collection)
        {
            return [];
        }

        var count = await items.CountPublishedItemsAsync(siteId, collection, cancellationToken).ConfigureAwait(false);
        return count > 0 ? [new TemplateWarningResponse("items_lose_address", count)] : [];
    }

    /// <summary>
    /// <c>item_address_hidden</c>: the page sits beside a template whose collection has a
    /// published item with the page's slug, so the page now answers that address (TB-03 order).
    /// </summary>
    public async Task<IReadOnlyList<TemplateWarningResponse>> AddressHiddenAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken)
    {
        var page = await database.ForSite<Page>(siteId)
            .AsNoTracking()
            .Where(p => p.Id == pageId)
            .Select(p => new { p.ParentId, p.Slug, p.TemplateCollectionId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (page is null || page.ParentId is null || page.TemplateCollectionId is not null || string.IsNullOrEmpty(page.Slug))
        {
            return [];
        }

        var sibling = await database.ForSite<Page>(siteId)
            .AsNoTracking()
            .Where(p => p.ParentId == page.ParentId && p.TemplateCollectionId != null)
            .Select(p => p.TemplateCollectionId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (sibling is not { } collection)
        {
            return [];
        }

        var itemId = await items.PublishedItemWithSlugAsync(siteId, collection, page.Slug, cancellationToken).ConfigureAwait(false);
        return itemId is { } hidden ? [new TemplateWarningResponse("item_address_hidden", ItemId: hidden)] : [];
    }

    /// <summary>The collections of the templates among <paramref name="pageIds"/> and their descendants.</summary>
    private async Task<HashSet<Guid>> TemplatesUnderAsync(Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken)
    {
        var pages = await database.ForSite<Page>(siteId)
            .AsNoTracking()
            .Select(p => new { p.Id, p.ParentId, p.TemplateCollectionId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var children = pages.Where(p => p.ParentId is not null).ToLookup(p => p.ParentId!.Value);
        var templates = new HashSet<Guid>();
        var pending = new Stack<Guid>(pageIds);
        var seen = new HashSet<Guid>();
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!seen.Add(id))
            {
                continue;
            }

            if (pages.Find(p => p.Id == id)?.TemplateCollectionId is { } collection)
            {
                templates.Add(collection);
            }

            foreach (var child in children[id])
            {
                pending.Push(child.Id);
            }
        }

        return templates;
    }
}
