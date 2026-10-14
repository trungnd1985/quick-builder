using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Binding;

/// <summary>
/// Fetches, before rendering, every published item, schema and template pattern a page's
/// bindings read (016 research TB-09), in a fixed number of batched reads per page shape.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The documents are walked for the reference fields bindings follow (<c>via</c>) and for
/// their Collection lists; each top-level list is one query (<see cref="ListQueries"/>).</item>
/// <item>The items those fields name, on every item already fetched, are read in one batch.
/// Only published items come back; a reference to anything else reads as empty.</item>
/// <item>The schemas of every collection involved, and the live template patterns of those
/// collections, for item links.</item>
/// </list>
/// The media files and pages that the fetched values show or link to are returned too, so the
/// renderer fetches them with the page's own (TB-09 step 3).
/// </remarks>
internal sealed class BindingFetcher(IPublishedItemReader items, IPublishedContentReader pages)
{
    /// <summary>What a page's bindings need, given its document, its components' documents and its item.</summary>
    public async Task<FetchedBindings> FetchAsync(
        Guid siteId,
        IReadOnlyList<JsonElement> documents,
        PublishedItem? pageItem,
        IReadOnlyDictionary<string, int>? pageNumbers,
        string listPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var viaFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (document.TryGetProperty("root", out var root))
            {
                CollectVia(root, viaFields);
            }
        }

        var fetched = new List<PublishedItem>();
        if (pageItem is not null)
        {
            fetched.Add(pageItem);
        }

        var listNodes = ListQueries.TopLevel(documents, PageSchema.MaxListsPerDocument);

        // Nothing reads an item: a page with no bindings and no lists costs no query.
        if (fetched.Count == 0 && listNodes.Count == 0)
        {
            return FetchedBindings.None;
        }

        // The page item's schema first: a list over one of its multi-reference fields needs the
        // field's target collection.
        var schemas = new Dictionary<Guid, JsonElement>();
        if (pageItem is not null)
        {
            foreach (var (id, schema) in await items.GetSchemasAsync(siteId, [pageItem.CollectionId], cancellationToken).ConfigureAwait(false))
            {
                schemas[id] = schema;
            }
        }

        var parsed = schemas.ToDictionary(s => s.Key, s => CollectionSchemaInfo.Parse(s.Key, s.Value));
        var lists = new Dictionary<string, PublishedItemPage>(StringComparer.Ordinal);
        var byQuery = new Dictionary<string, PublishedItemPage>(StringComparer.Ordinal);
        var skipped = new List<string>();

        // Level by level (TB-09): the top-level lists in the page item's scope, then the lists in
        // their cards, once per listed item, with that item's scope open. Identical queries, such
        // as cards with the same outer value, run once.
        IReadOnlyList<ScopedItem> pageScopes = pageItem is null ? [] : [new(ListQueries.Scope(ListScope.Page, pageItem, parsed), pageItem.Id)];
        var level = listNodes.Select(node => (Node: node, Scopes: pageScopes)).ToList();
        for (var depth = 1; depth <= PageSchema.MaxListDepth && level.Count > 0; depth++)
        {
            var listed = new List<(JsonElement Node, IReadOnlyList<ScopedItem> Scopes, IReadOnlyList<PublishedItem> Items)>();
            foreach (var (node, scopes) in level)
            {
                var id = RenderContext.IdOf(node);
                var key = ResolvedData.ListKey(id, scopes.Select(s => s.ItemId));
                var props = node.TryGetProperty("props", out var p) ? p : default;
                var pageNumber = pageNumbers is not null && pageNumbers.TryGetValue(ListQueries.PageParam(props), out var number) ? number : 1;
                var query = ListQueryBuilder.Build(props, [.. scopes.Select(s => s.Scope)], pageNumber);

                PublishedItemPage result;
                if (query is null)
                {
                    result = PublishedItemPage.Empty;
                }
                else if (byQuery.TryGetValue(ListQueries.Identity(query), out var same))
                {
                    result = same;
                }
                else if (byQuery.Count >= ListQueries.MaxQueriesPerRender)
                {
                    // TB-09: past the budget, a list shows its empty state; the responder logs which.
                    skipped.Add(id);
                    result = PublishedItemPage.Empty;
                }
                else
                {
                    result = await items.QueryAsync(siteId, query, cancellationToken).ConfigureAwait(false);
                    byQuery[ListQueries.Identity(query)] = result;
                }

                lists[key] = result;
                fetched.AddRange(result.Items);
                listed.Add((node, scopes, result.Items));
            }

            // The next level's scopes need their items' schemas, for multi-reference sources.
            var unknown = listed.SelectMany(l => l.Items).Select(i => i.CollectionId).Where(c => !schemas.ContainsKey(c)).ToHashSet();
            if (unknown.Count > 0 && depth < PageSchema.MaxListDepth)
            {
                foreach (var (collection, schema) in await items.GetSchemasAsync(siteId, unknown, cancellationToken).ConfigureAwait(false))
                {
                    schemas[collection] = schema;
                    parsed[collection] = CollectionSchemaInfo.Parse(collection, schema);
                }
            }

            level = [.. listed.SelectMany(l => ListQueries.InCard(l.Node).SelectMany(inner => l.Items.Select(item => (
                Node: inner,
                Scopes: (IReadOnlyList<ScopedItem>)[.. l.Scopes, new ScopedItem(ListQueries.Scope(RenderContext.IdOf(l.Node), item, parsed), item.Id)]))))];
        }

        var referenced = new Dictionary<Guid, PublishedItem>();
        var referenceIds = fetched
            .SelectMany(item => viaFields.Select(field => BindingResolver.ReadValue(item, field)))
            .Where(v => v is { ValueKind: JsonValueKind.String })
            .Select(v => Guid.TryParse(v!.Value.GetString(), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        if (referenceIds.Count > 0)
        {
            foreach (var (id, item) in await items.GetAsync(siteId, referenceIds, cancellationToken).ConfigureAwait(false))
            {
                referenced[id] = item;
            }
        }

        var all = fetched.Concat(referenced.Values).ToList();
        var collections = all.Select(i => i.CollectionId).ToHashSet();

        var missing = collections.Where(id => !schemas.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            foreach (var (id, schema) in await items.GetSchemasAsync(siteId, missing, cancellationToken).ConfigureAwait(false))
            {
                schemas[id] = schema;
            }
        }

        var patterns = await pages.GetLiveTemplatePatternsAsync(siteId, collections, cancellationToken).ConfigureAwait(false);

        var media = new HashSet<Guid>();
        var links = new HashSet<Guid>();
        foreach (var item in all)
        {
            CollectShown(item.Values, media, links);
        }

        var data = new ResolvedData
        {
            PageItem = pageItem,
            Items = referenced,
            Lists = lists,
            PageNumbers = pageNumbers ?? new Dictionary<string, int>(StringComparer.Ordinal),
            ListPath = listPath,
            Schemas = schemas.ToDictionary(s => s.Key, s => CollectionSchemaInfo.Parse(s.Key, s.Value)),
            TemplatePatterns = patterns,
        };

        return new FetchedBindings(data, media, links, skipped);
    }

    /// <summary>A scope a list can read, and the id of its item, for the list's key.</summary>
    private sealed record ScopedItem(ListScope Scope, Guid ItemId);

    /// <summary>The reference fields that bindings follow, anywhere in a document.</summary>
    private static void CollectVia(JsonElement node, HashSet<string> fields)
    {
        if (node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            if (props.TryGetProperty("bind", out var bind))
            {
                CollectBindingVia(bind, fields);
            }

            // 017 research SE-04: the page's metadata bindings read through references too.
            if (props.TryGetProperty("seo", out var seo) && seo.ValueKind == JsonValueKind.Object && seo.TryGetProperty("bind", out var seoBind))
            {
                CollectBindingVia(seoBind, fields);
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                CollectVia(child, fields);
            }
        }
    }

    /// <summary>The reference fields one node's bindings follow.</summary>
    private static void CollectBindingVia(JsonElement bind, HashSet<string> fields)
    {
        if (bind.ValueKind == JsonValueKind.Object)
        {
            foreach (var binding in bind.EnumerateObject())
            {
                if (!binding.Value.TryGetProperty("value", out var value))
                {
                    continue;
                }

                IEnumerable<JsonElement> readers = value.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array
                    ? parts.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object)
                    : [value];

                foreach (var reader in readers)
                {
                    if (reader.TryGetProperty("via", out var via) && via.GetString() is { } field)
                    {
                        fields.Add(field);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Every media file and page an item's values show or link to: <c>{ mediaId }</c> values and
    /// rich text images, and rich text links to pages. A superset of what is bound is harmless.
    /// </summary>
    private static void CollectShown(JsonElement value, HashSet<Guid> media, HashSet<Guid> links)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (value.TryGetProperty("mediaId", out var mediaId) && Guid.TryParse(mediaId.GetString(), out var file))
                {
                    media.Add(file);
                }

                if (value.TryGetProperty("type", out var type) && type.GetString() == "page"
                    && value.TryGetProperty("pageId", out var pageId) && Guid.TryParse(pageId.GetString(), out var page))
                {
                    links.Add(page);
                }

                foreach (var property in value.EnumerateObject())
                {
                    CollectShown(property.Value, media, links);
                }

                break;
            case JsonValueKind.Array:
                foreach (var element in value.EnumerateArray())
                {
                    CollectShown(element, media, links);
                }

                break;
        }
    }
}

/// <summary>What a page's bindings read, and the media files and pages its bound values need.</summary>
/// <param name="SkippedLists">The ids of lists left empty because the render's query budget was spent (TB-09).</param>
internal sealed record FetchedBindings(ResolvedData Data, IReadOnlySet<Guid> Media, IReadOnlySet<Guid> Pages, IReadOnlyList<string>? SkippedLists = null)
{
    public static FetchedBindings None { get; } = new(ResolvedData.Empty, new HashSet<Guid>(), new HashSet<Guid>());
}
