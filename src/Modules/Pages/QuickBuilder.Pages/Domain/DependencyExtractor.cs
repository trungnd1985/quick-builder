using System.Text.Json;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// What a published document depends on: the components it shows, the pages it links to and
/// the media files it shows (012 research PR-04, data-model §1.3).
/// </summary>
/// <remarks>
/// Read from the document when it is published, so the graph is complete before anyone can
/// request the version. The document has passed the page schema, so every shape read here is
/// the contract's; anything else is ignored rather than trusted.
/// </remarks>
/// <summary>A document's paginated lists and component references, each with its JSON Pointer.</summary>
internal sealed record PageParamSlots(
    IReadOnlyList<(string Name, string Pointer)> Lists,
    IReadOnlyList<(Guid ComponentId, string Pointer)> Components);

internal static class DependencyExtractor
{
    /// <summary>The distinct targets of <paramref name="document"/>, as (kind, id).</summary>
    public static IReadOnlyList<(string Kind, Guid Id)> Extract(string document)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        using var parsed = JsonDocument.Parse(document);
        var found = new HashSet<(string, Guid)>();

        if (parsed.RootElement.TryGetProperty("root", out var root))
        {
            Walk(root, found);
        }

        return [.. found.OrderBy(t => t.Item1, StringComparer.Ordinal).ThenBy(t => t.Item2)];
    }

    /// <summary>The components a document references.</summary>
    public static IReadOnlyList<Guid> Components(string document) =>
        [.. Extract(document).Where(t => t.Kind == DependencyKinds.Component).Select(t => t.Id)];

    /// <summary>The media files a document shows (013 research MV-05).</summary>
    public static IReadOnlyList<Guid> Media(string document) =>
        [.. Extract(document).Where(t => t.Kind == DependencyKinds.Media).Select(t => t.Id)];

    /// <summary>
    /// The multi-reference fields that lists take their items from (016 FR-025): their target
    /// collections are known only from a schema, so the publisher resolves them.
    /// </summary>
    public static IReadOnlySet<string> ListSourceFields(string document) =>
        CollectLists(document, (props, found) =>
        {
            if (props.TryGetProperty("source", out var source) && source.TryGetProperty("field", out var field) && field.GetString() is { } id)
            {
                found.Add(id);
            }
        });

    /// <summary>
    /// The page parameters of a document's paginated lists, <c>page</c> when one names none
    /// (016 research TB-11): the public path reads them before the document, for the cache key.
    /// </summary>
    public static IReadOnlySet<string> PageParamNames(string document) =>
        CollectLists(document, (props, found) =>
        {
            if (props.TryGetProperty("paging", out var paging) && paging.ValueKind == JsonValueKind.Object)
            {
                found.Add(paging.TryGetProperty("param", out var param) && param.GetString() is { } name ? name : "page");
            }
        });

    /// <summary>
    /// What a document brings to its page's page parameters (016 FR-027, contract rule 21): each
    /// paginated list's parameter and each component reference, in document order, with the
    /// JSON Pointer a refusal names (<c>…/props/paging/param</c>, or <c>…/props/paging</c> when
    /// the list names none, and the reference node itself).
    /// </summary>
    public static PageParamSlots PageParamSlots(string document)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        using var parsed = JsonDocument.Parse(document);
        var lists = new List<(string Name, string Pointer)>();
        var components = new List<(Guid ComponentId, string Pointer)>();
        if (parsed.RootElement.TryGetProperty("root", out var root))
        {
            Visit(root, "/root");
        }

        return new PageParamSlots(lists, components);

        void Visit(JsonElement node, string pointer)
        {
            var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                if (type == "collection-list" && props.TryGetProperty("paging", out var paging) && paging.ValueKind == JsonValueKind.Object)
                {
                    lists.Add(paging.TryGetProperty("param", out var param) && param.GetString() is { } name
                        ? (name, pointer + "/props/paging/param")
                        : ("page", pointer + "/props/paging"));
                }
                else if (type == "component-ref" && props.TryGetProperty("componentId", out var id) && Guid.TryParse(id.GetString(), out var componentId))
                {
                    components.Add((componentId, pointer));
                }
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var child in children.EnumerateArray())
                {
                    Visit(child, $"{pointer}/children/{index++}");
                }
            }
        }
    }

    private static HashSet<string> CollectLists(string document, Action<JsonElement, HashSet<string>> read)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        using var parsed = JsonDocument.Parse(document);
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (parsed.RootElement.TryGetProperty("root", out var root))
        {
            Visit(root);
        }

        return found;

        void Visit(JsonElement node)
        {
            if (node.TryGetProperty("type", out var type) && type.GetString() == "collection-list"
                && node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                read(props, found);
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    Visit(child);
                }
            }
        }
    }

    private static void Walk(JsonElement node, HashSet<(string, Guid)> found)
    {
        if (node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;

            switch (type)
            {
                case "component-ref":
                    AddGuid(found, DependencyKinds.Component, props, "componentId");
                    break;
                case "button":
                    AddPageLink(found, props, "link");
                    break;
                case "image":
                    AddPageLink(found, props, "link");
                    if (props.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
                    {
                        AddGuid(found, DependencyKinds.Media, media, "mediaId");
                    }

                    break;
                case "rich-text":
                    if (props.TryGetProperty("content", out var content))
                    {
                        WalkRichText(content, found);
                    }

                    break;
                case "collection-list":
                    // 016 data-model §1.2: a list depends on the collection it shows.
                    if (props.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
                    {
                        AddGuid(found, DependencyKinds.Collection, source, "collectionId");
                    }

                    break;
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, found);
            }
        }
    }

    private static void WalkRichText(JsonElement node, HashSet<(string, Guid)> found)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // 035: a rich text image shows a media file, for usage and invalidation alike (RT-11).
        if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "image"
            && node.TryGetProperty("attrs", out var imageAttrs) && imageAttrs.ValueKind == JsonValueKind.Object
            && imageAttrs.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
        {
            AddGuid(found, DependencyKinds.Media, media, "mediaId");
        }

        if (node.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
        {
            foreach (var mark in marks.EnumerateArray())
            {
                if (mark.ValueKind == JsonValueKind.Object
                    && mark.TryGetProperty("type", out var markType) && markType.GetString() == "link"
                    && mark.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                {
                    AddPageLink(found, attrs, "to");
                }
            }
        }

        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                WalkRichText(child, found);
            }
        }
    }

    private static void AddPageLink(HashSet<(string, Guid)> found, JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var link)
            && link.ValueKind == JsonValueKind.Object
            && link.TryGetProperty("type", out var type) && type.GetString() == "page")
        {
            AddGuid(found, DependencyKinds.Page, link, "pageId");
        }
    }

    private static void AddGuid(HashSet<(string, Guid)> found, string kind, JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var id))
        {
            found.Add((kind, id));
        }
    }
}
