using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// <see cref="ICollectionSchemaReader"/> for other modules' admin-side checks (016 research
/// TB-06, TB-08, TB-10).
/// </summary>
/// <remarks>
/// Reads through <see cref="CmsDbContext.ForSite{T}"/>: callers are on the administrative
/// surface, or in an event handler, where no site is resolved, and name the site explicitly.
/// A site holds tens of collections at most, so scanning their schemas is cheap.
/// </remarks>
internal sealed class CollectionSchemaReader(CmsDbContext database) : ICollectionSchemaReader
{
    public async Task<IReadOnlySet<Guid>> ExistingAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionIds);

        if (collectionIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = collectionIds.Distinct().ToList();
        var found = await database.ForSite<Collection>(siteId)
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return found.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<Guid, JsonElement>> GetSchemasAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionIds);

        if (collectionIds.Count == 0)
        {
            return new Dictionary<Guid, JsonElement>();
        }

        var ids = collectionIds.Distinct().ToList();
        var rows = await database.ForSite<Collection>(siteId)
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Schema })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.Id, r =>
        {
            using var parsed = JsonDocument.Parse(r.Schema);
            return parsed.RootElement.Clone();
        });
    }

    public async Task<IReadOnlyList<Guid>> CollectionsReferencingAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default)
    {
        var schemas = await database.ForSite<Collection>(siteId)
            .AsNoTracking()
            .Select(c => new { c.Id, c.Schema })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. schemas
            .Where(s => CollectionSchemaDocument.Read(s.Schema).Fields
                .Any(f => f.Type is FieldType.Reference or FieldType.MultiReference && f.Target == collectionId))
            .Select(s => s.Id)];
    }

    public Task<int> CountPublishedItemsAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default) =>
        database.ForSite<Item>(siteId).CountAsync(i => i.CollectionId == collectionId && i.PublishedSlug != null, cancellationToken);
}
