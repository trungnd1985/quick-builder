using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.Rendering.Seo.StructuredData;

/// <summary>
/// A page's structured data as one JSON-LD graph, safe inside a <c>&lt;script&gt;</c> element
/// (017 FR-036; research SE-18; public-seo.md §4).
/// </summary>
/// <remarks>
/// The default encoder escapes <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c>, <c>+</c> and
/// every character outside ASCII, among them U+2028 and U+2029, so no value can end the element
/// or break a script parser. The explicit replacement keeps that true even if the encoder's
/// defaults change. The element is data: the browser never runs it, and the page's policy has no
/// <c>script-src</c>.
/// </remarks>
internal static class JsonLdWriter
{
    private static readonly string LineSeparator = ((char)0x2028).ToString();

    private static readonly string ParagraphSeparator = ((char)0x2029).ToString();

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.Default, WriteIndented = false };

    /// <summary>The graph of <paramref name="nodes"/>, or null when there is nothing to describe.</summary>
    public static string? Write(IReadOnlyList<JsonObject> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
        {
            return null;
        }

        var graph = new JsonArray();
        foreach (var node in nodes)
        {
            graph.Add(node.DeepClone());
        }

        var document = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = graph,
        };

        return document.ToJsonString(Options)
            .Replace(LineSeparator, @"\u2028", StringComparison.Ordinal)
            .Replace(ParagraphSeparator, @"\u2029", StringComparison.Ordinal);
    }
}
