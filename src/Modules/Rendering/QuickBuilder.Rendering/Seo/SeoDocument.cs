using System.Text.Json;

namespace QuickBuilder.Rendering.Seo;

/// <summary>
/// A live document's search metadata, <c>root.props.seo</c> (017 research SE-02,
/// seo-metadata.md §1).
/// </summary>
/// <remarks>
/// Read from a document the Pages module validated when it was saved, so this never refuses:
/// a missing or empty text is simply absent, an unknown key is ignored. Bindings, fallbacks
/// and the structured data mapping are kept as cloned JSON for the resolver, so they outlive
/// the document they came from. Every text is content: render it encoded.
/// </remarks>
internal sealed record SeoDocument(
    string? Title,
    string? Description,
    string? OgTitle,
    string? OgDescription,
    Guid? OgImageId,
    SeoCanonical? Canonical,
    bool NoIndex,
    bool NoSitemap,
    IReadOnlyDictionary<string, JsonElement> Bindings,
    IReadOnlyDictionary<string, string> Fallbacks,
    string? StructuredDataType,
    IReadOnlyDictionary<string, JsonElement> StructuredDataBindings)
{
    /// <summary>The metadata of a document envelope, or null when its root has none.</summary>
    public static SeoDocument? From(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty("root", out var root)
            || !root.TryGetProperty("props", out var props)
            || !props.TryGetProperty("seo", out var seo)
            || seo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? structuredType = null;
        IReadOnlyDictionary<string, JsonElement> structuredBindings = Empty;
        if (seo.TryGetProperty("structuredData", out var structured) && structured.ValueKind == JsonValueKind.Object)
        {
            structuredType = Text(structured, "type");
            structuredBindings = Objects(structured, "bind");
        }

        return new SeoDocument(
            Text(seo, "title"),
            Text(seo, "description"),
            Text(seo, "ogTitle"),
            Text(seo, "ogDescription"),
            seo.TryGetProperty("ogImage", out var image) && image.ValueKind == JsonValueKind.Object
                && image.TryGetProperty("mediaId", out var mediaId) && mediaId.TryGetGuid(out var id)
                ? id
                : null,
            ReadCanonical(seo),
            Flag(seo, "noindex"),
            Flag(seo, "noSitemap"),
            Objects(seo, "bind"),
            Texts(seo, "fallback"),
            structuredType,
            structuredBindings);
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> Empty = new Dictionary<string, JsonElement>();

    private static string? Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static SeoCanonical? ReadCanonical(JsonElement seo)
    {
        if (!seo.TryGetProperty("canonical", out var canonical) || canonical.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Text(canonical, "type") switch
        {
            "page" when canonical.TryGetProperty("pageId", out var pageId) && pageId.TryGetGuid(out var id) => new SeoCanonical(id, null),
            "url" when Text(canonical, "href") is { } href => new SeoCanonical(null, href),
            _ => null,
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> Objects(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var map) || map.ValueKind != JsonValueKind.Object)
        {
            return Empty;
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in map.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object)
            {
                result[entry.Name] = entry.Value.Clone();
            }
        }

        return result;
    }

    private static Dictionary<string, string> Texts(JsonElement owner, string name)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (owner.TryGetProperty(name, out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject())
            {
                if (Text(map, entry.Name) is { } text)
                {
                    result[entry.Name] = text;
                }
            }
        }

        return result;
    }
}

/// <summary>A canonical override: a page of the site, or an absolute address (017 FR-012).</summary>
internal sealed record SeoCanonical(Guid? PageId, string? Href);
