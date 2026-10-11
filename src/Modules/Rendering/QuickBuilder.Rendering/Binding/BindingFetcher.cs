using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;

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

        var pageSchemas = schemas.ToDictionary(s => s.Key, s => CollectionSchemaInfo.Parse(s.Key, s.Value));
        var lists = new Dictionary<string, PublishedItemPage>(StringComparer.Ordinal);
        foreach (var node in listNodes)
        {
            var planned = ListQueries.Plan(node, pageItem, pageSchemas, pageNumbers);
            var result = planned.Query is { } query
                ? await items.QueryAsync(siteId, query, cancellationToken).ConfigureAwait(false)
                : PublishedItemPage.Empty;
            lists[planned.Key] = result;
            fetched.AddRange(result.Items);
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

        return new FetchedBindings(data, media, links);
    }

    /// <summary>The reference fields that bindings follow, anywhere in a document.</summary>
    private static void CollectVia(JsonElement node, HashSet<string> fields)
    {
        if (node.TryGetProperty("props", out var props) && props.TryGetProperty("bind", out var bind) && bind.ValueKind == JsonValueKind.Object)
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

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                CollectVia(child, fields);
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
internal sealed record FetchedBindings(ResolvedData Data, IReadOnlySet<Guid> Media, IReadOnlySet<Guid> Pages)
{
    public static FetchedBindings None { get; } = new(ResolvedData.Empty, new HashSet<Guid>(), new HashSet<Guid>());
}
