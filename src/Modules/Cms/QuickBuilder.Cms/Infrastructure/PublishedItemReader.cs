using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts.Publishing;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// <see cref="IPublishedItemReader"/> over the read-only context (016 research TB-08).
/// </summary>
/// <remarks>
/// <see cref="FindBySlugAsync"/> runs on every request for an item page, so it is a compiled
/// query on <c>ix_items_collection_id_published_slug</c>. An item's name is read from its
/// published values through its collection's title field, never from the draft-derived
/// <c>name</c> column the admin list sorts on.
/// </remarks>
internal sealed class PublishedItemReader(CmsPublishedReadDbContext database) : IPublishedItemReader
{
    private static readonly Func<CmsPublishedReadDbContext, Guid, Guid, string, CancellationToken, Task<PublishedItemRef?>> BySlug =
        EF.CompileAsyncQuery((CmsPublishedReadDbContext db, Guid siteId, Guid collectionId, string slug, CancellationToken _) =>
            db.Items
                .Where(i => i.SiteId == siteId && i.CollectionId == collectionId && i.PublishedSlug == slug)
                .Select(i => new PublishedItemRef(i.Id, i.RenderStamp))
                .FirstOrDefault());

    public Task<PublishedItemRef?> FindBySlugAsync(Guid siteId, Guid collectionId, string slug, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);
        return BySlug(database, siteId, collectionId, slug, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, PublishedItem>> GetAsync(
        Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, PublishedItem>();
        }

        var ids = itemIds.Distinct().ToList();
        var rows = await database.Items
            .Where(i => i.SiteId == siteId && ids.Contains(i.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToItemsAsync(siteId, rows, cancellationToken).ConfigureAwait(false);
    }

    public Task<PublishedItemPage> QueryAsync(Guid siteId, PublishedListQuery query, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Collection list queries arrive with the Collection list block (016 US4, T064).");

    public async Task<IReadOnlyDictionary<Guid, JsonElement>> GetSchemasAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionIds);

        if (collectionIds.Count == 0)
        {
            return new Dictionary<Guid, JsonElement>();
        }

        var ids = collectionIds.Distinct().ToList();
        var rows = await database.Collections
            .Where(c => c.SiteId == siteId && ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Schema })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.Id, r => Parse(r.Schema));
    }

    public Task<int> CountPublishedAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default) =>
        database.Items.CountAsync(i => i.SiteId == siteId && i.CollectionId == collectionId, cancellationToken);

    /// <summary>Rows as contract items, each named by its collection's title field.</summary>
    internal async Task<Dictionary<Guid, PublishedItem>> ToItemsAsync(Guid siteId, IReadOnlyList<PublishedItemRow> rows, CancellationToken cancellationToken)
    {
        var titles = await TitleFieldsAsync(siteId, [.. rows.Select(r => r.CollectionId).Distinct()], cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<Guid, PublishedItem>(rows.Count);
        foreach (var row in rows)
        {
            using (row)
            {
                result[row.Id] = ToItem(row, titles.GetValueOrDefault(row.CollectionId));
            }
        }

        return result;
    }

    /// <summary>A row as a contract item, its values detached from the row's buffer.</summary>
    internal static PublishedItem ToItem(PublishedItemRow row, string? titleFieldId)
    {
        ArgumentNullException.ThrowIfNull(row);

        var values = row.Published?.RootElement.Clone() ?? default;
        string? name = null;
        if (titleFieldId is not null
            && values.ValueKind == JsonValueKind.Object
            && values.TryGetProperty("values", out var fields)
            && fields.TryGetProperty(titleFieldId, out var title)
            && title.ValueKind == JsonValueKind.String)
        {
            name = title.GetString();
        }

        var last = row.LastPublishedAtUtc ?? DateTimeOffset.MinValue;
        return new PublishedItem(row.Id, row.CollectionId, row.PublishedSlug!, name, row.FirstPublishedAtUtc ?? last, last, values);
    }

    /// <summary>Each collection's title field id, from its current schema.</summary>
    internal async Task<Dictionary<Guid, string?>> TitleFieldsAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken)
    {
        var schemas = await GetSchemasAsync(siteId, collectionIds, cancellationToken).ConfigureAwait(false);
        return schemas.ToDictionary(
            s => s.Key,
            s => s.Value.TryGetProperty("titleFieldId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null);
    }

    private static JsonElement Parse(string document)
    {
        using var parsed = JsonDocument.Parse(document);
        return parsed.RootElement.Clone();
    }
}
