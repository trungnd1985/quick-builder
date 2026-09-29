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
