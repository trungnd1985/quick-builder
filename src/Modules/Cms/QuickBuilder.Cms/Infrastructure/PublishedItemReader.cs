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

    public async Task<PublishedItemPage> QueryAsync(Guid siteId, PublishedListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Count <= 0 || query.Offset < 0 || query.ItemIds is { Count: 0 })
        {
            return PublishedItemPage.Empty;
        }

        var schemas = await GetSchemasAsync(siteId, [query.CollectionId], cancellationToken).ConfigureAwait(false);
        if (!schemas.TryGetValue(query.CollectionId, out var schemaDocument))
        {
            return PublishedItemPage.Empty;
        }

        var schema = ListSchema.Read(schemaDocument);
        var items = database.Items.Where(i => i.SiteId == siteId && i.CollectionId == query.CollectionId);
        if (query.ItemIds is { } itemIds)
        {
            var ids = itemIds.Distinct().ToList();
            items = items.Where(i => ids.Contains(i.Id));
        }

        var filtered = PublishedListTranslation.Filter(items, database.References.Where(r => r.SiteId == siteId), schema, query.Filters);
        if (filtered is null)
        {
            // A broken condition matches nothing, never everything (§5).
            return PublishedItemPage.Empty;
        }

        List<PublishedItemRow> rows;
        int total;
        if (query.ItemIds is { } order && query.Sorts.Count == 0)
        {
            // A multi-reference source keeps the field's order; it holds few ids, so the page is
            // cut here rather than in SQL.
            var position = order.Select((id, index) => (id, index)).GroupBy(p => p.id).ToDictionary(g => g.Key, g => g.First().index);
            var all = await filtered.ToListAsync(cancellationToken).ConfigureAwait(false);
            total = all.Count;
            rows = [.. all.OrderBy(r => position[r.Id]).Skip(query.Offset).Take(query.Count)];
            foreach (var skipped in all.Except(rows))
            {
                skipped.Dispose();
            }
        }
        else
        {
            total = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
            rows = total <= query.Offset
                ? []
                : await PublishedListTranslation.Sort(filtered, schema, query.Sorts)
                    .Skip(query.Offset)
                    .Take(query.Count)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
        }

        var byId = await ToItemsAsync(siteId, rows, cancellationToken).ConfigureAwait(false);
        return new PublishedItemPage([.. rows.Select(r => byId[r.Id])], total);
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
        var rows = await database.Collections
            .Where(c => c.SiteId == siteId && ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Schema })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.Id, r => Parse(r.Schema));
    }

    public Task<int> CountPublishedAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default) =>
        database.Items.CountAsync(i => i.SiteId == siteId && i.CollectionId == collectionId, cancellationToken);

    /// <summary>Sitemap addresses; built with the sitemap (017 tasks T058).</summary>
    public Task<IReadOnlyList<PublishedItemAddress>> ListAddressesAsync(
        Guid siteId, Guid collectionId, string? hideFieldId, Guid? afterId, int limit, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("017 T058: sitemap addresses are not built yet.");

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
