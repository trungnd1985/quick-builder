using System.Text.Json;

namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// The components, link targets and media files a render needs, found in one pass over the
/// documents so each is read in one batch (012 research PR-09).
/// </summary>
internal sealed class RenderReferences
{
    public HashSet<Guid> Components { get; } = [];

    public HashSet<Guid> Pages { get; } = [];

    public HashSet<Guid> Media { get; } = [];

    /// <summary>Adds everything <paramref name="document"/> refers to.</summary>
    public void Collect(JsonElement document)
    {
        if (document.ValueKind == JsonValueKind.Object && document.TryGetProperty("root", out var root))
        {
            Walk(root);
        }
    }

    private void Walk(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            switch (node.TryGetProperty("type", out var type) ? type.GetString() : null)
            {
                case "component-ref":
                    AddGuid(Components, props, "componentId");
                    break;
                case "button":
                    AddLink(props, "link");
                    break;
                case "image":
                    AddLink(props, "link");
                    if (props.TryGetProperty("media", out var media))
                    {
                        AddGuid(Media, media, "mediaId");
                    }

                    break;
                case "rich-text":
                    if (props.TryGetProperty("content", out var content))
                    {
                        WalkRichText(content);
                    }

                    break;
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child);
            }
        }
    }

    private void WalkRichText(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // 035: a rich text image shows a media file.
        if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "image"
            && node.TryGetProperty("attrs", out var imageAttrs) && imageAttrs.ValueKind == JsonValueKind.Object
            && imageAttrs.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
        {
            AddGuid(Media, media, "mediaId");
        }

        if (node.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
        {
            foreach (var mark in marks.EnumerateArray())
            {
                if (mark.ValueKind == JsonValueKind.Object && mark.TryGetProperty("attrs", out var attrs))
                {
                    AddLink(attrs, "to");
                }
            }
        }

        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                WalkRichText(child);
            }
        }
    }

    private void AddLink(JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out var link)
            && link.ValueKind == JsonValueKind.Object
            && link.TryGetProperty("type", out var type) && type.GetString() == "page")
        {
            AddGuid(Pages, link, "pageId");
        }
    }

    private static void AddGuid(HashSet<Guid> set, JsonElement parent, string property)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var id))
        {
            set.Add(id);
        }
    }
}
