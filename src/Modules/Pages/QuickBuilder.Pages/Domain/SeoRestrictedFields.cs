using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// The search metadata only members who shape the page structure may change: the canonical
/// address and hiding from search engines, fixed or bound (017 FR-002, research SE-03).
/// </summary>
/// <remarks>
/// A draft save compares them with the current draft rather than refusing any document that
/// holds them, so an Editor can keep editing a page whose canonical address a Designer set.
/// </remarks>
internal static class SeoRestrictedFields
{
    /// <summary>The fields, as the refusal names them.</summary>
    public static IReadOnlyList<string> All { get; } = ["canonical", "noindex", "bind.noindex"];

    /// <summary>
    /// The first restricted field whose value differs between the two documents, or null when
    /// none does. Absent and <c>false</c> are the same for <c>noindex</c>.
    /// </summary>
    public static string? FirstChanged(string currentDocument, string newDocument)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(newDocument);

        var before = Seo(currentDocument);
        var after = Seo(newDocument);

        if (!JsonNode.DeepEquals(before?["canonical"], after?["canonical"]))
        {
            return "canonical";
        }

        if (IsTrue(before?["noindex"]) != IsTrue(after?["noindex"]))
        {
            return "noindex";
        }

        return JsonNode.DeepEquals(before?["bind"]?["noindex"], after?["bind"]?["noindex"]) ? null : "bind.noindex";
    }

    private static JsonObject? Seo(string document) =>
        JsonNode.Parse(document)?["root"]?["props"]?["seo"] as JsonObject;

    private static bool IsTrue(JsonNode? value) =>
        value is JsonValue v && v.GetValueKind() == JsonValueKind.True;
}
