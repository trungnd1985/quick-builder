using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// The read side every collection response goes through: one collection with its item count
/// and the name of whoever changed it last, and the site's list (FR-003, FR-013).
/// </summary>
internal sealed class CollectionReads(CmsDbContext database, CmsAccess access)
{
    private static readonly IReadOnlyDictionary<string, string> NoIds = new Dictionary<string, string>();

    public async Task<CollectionResponse?> OneAsync(
        Guid siteId,
        Guid collectionId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? idMap = null)
    {
        var collection = await database.ForSite<Collection>(siteId)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == collectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return null;
        }

        var itemCount = await database.ForSite<Item>(siteId)
            .CountAsync(i => i.CollectionId == collectionId, cancellationToken)
            .ConfigureAwait(false);

        return await ToResponseAsync(collection, itemCount, idMap ?? NoIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CollectionResponse> ToResponseAsync(
        Collection collection,
        int itemCount,
        IReadOnlyDictionary<string, string> idMap,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var names = await access.NamesAsync([collection.UpdatedBy], cancellationToken).ConfigureAwait(false);
        var canManage = await access.CanManageAsync(collection.SiteId, cancellationToken).ConfigureAwait(false);

        return new CollectionResponse(
            collection.Id,
            collection.Name,
            collection.Slug,
            collection.Description,
            JsonNode.Parse(collection.Schema)!,
            collection.Revision,
            collection.SchemaRevision,
            itemCount,
            canManage,
            collection.CreatedAtUtc,
            collection.UpdatedAtUtc,
            new PersonResponse(collection.UpdatedBy, names.GetValueOrDefault(collection.UpdatedBy, string.Empty)),
            idMap);
    }

    public async Task<CollectionListResponse> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var collections = await database.ForSite<Collection>(siteId)
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var counts = await database.ForSite<Item>(siteId)
            .GroupBy(i => i.CollectionId)
            .Select(g => new { CollectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CollectionId, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

        var summaries = collections
            .Select(c => new CollectionSummaryResponse(
                c.Id,
                c.Name,
                c.Slug,
                c.SchemaDocument.Fields.Count,
                counts.GetValueOrDefault(c.Id),
                c.Revision,
                c.UpdatedAtUtc))
            .ToList();

        return new CollectionListResponse(summaries, await access.CanManageAsync(siteId, cancellationToken).ConfigureAwait(false));
    }
}
