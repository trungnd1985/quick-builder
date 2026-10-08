using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.Items;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// The items that refer to an item, with their collection and field names (015 FR-008, FR-021;
/// research IT-10), read from <c>item_references</c>.
/// </summary>
/// <remarks>Up to <see cref="Shown"/> are named, with the total. A reference from the item to itself is never stored.</remarks>
internal sealed class ItemReferrers(CmsDbContext database)
{
    public const int Shown = 50;

    public async Task<ReferrerListResponse> ListAsync(Guid siteId, Guid itemId, bool publishedOnly, CancellationToken cancellationToken)
    {
        var references = database.ForSite<ItemReference>(siteId)
            .AsNoTracking()
            .Where(r => r.TargetItemId == itemId && (!publishedOnly || r.InPublished));

        var total = await references.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return new ReferrerListResponse([], 0);
        }

        var rows = await references
            .Join(database.ForSite<Item>(siteId), r => r.SourceItemId, i => i.Id, (r, i) => new
            {
                r.SourceItemId,
                r.FieldId,
                r.InDraft,
                r.InPublished,
                r.InScheduled,
                i.Name,
                i.Slug,
                i.CollectionId,
            })
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Slug)
            .ThenBy(r => r.FieldId)
            .Take(Shown)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var collectionIds = rows.Select(r => r.CollectionId).Distinct().ToList();
        var collections = (await database.ForSite<Collection>(siteId)
                .AsNoTracking()
                .Where(c => collectionIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Schema })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(c => c.Id, c => (c.Name, Schema: CollectionSchemaDocument.Read(c.Schema)));

        return new ReferrerListResponse(
            rows.Select(r =>
            {
                var (collectionName, schema) = collections[r.CollectionId];
                return new ReferrerResponse(
                    r.SourceItemId,
                    r.Name,
                    r.Slug,
                    r.CollectionId,
                    collectionName,
                    r.FieldId,
                    schema.Find(r.FieldId)?.Name ?? r.FieldId,
                    r.InDraft,
                    r.InPublished,
                    r.InScheduled);
            }).ToList(),
            total);
    }
}
