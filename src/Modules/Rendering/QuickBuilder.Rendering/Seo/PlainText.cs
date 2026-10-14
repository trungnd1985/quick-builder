using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickBuilder.Rendering.Seo;

/// <summary>
/// Metadata values as one line of text (017 FR-008, research SE-04): rich text as its words,
/// whitespace collapsed, and a value over the field's limit cut at a word with <c>…</c>.
/// </summary>
/// <remarks>The admin's <c>editor/seo/plainText.ts</c> is a port, and both run the same golden cases.</remarks>
internal static partial class PlainText
{
    /// <summary>What is added where a value was cut.</summary>
    public const string Ellipsis = "…";

    /// <summary>The longest value of each metadata text field, as the page schema allows.</summary>
    public static int MaxLength(string field) => field switch
    {
        "title" or "ogTitle" => 200,
        _ => 500,
    };

    /// <summary>A rich text document's text: block texts joined with spaces, marks dropped.</summary>
    public static string Of(JsonElement richText)
    {
        var text = new StringBuilder();
        Append(richText, text);
        return Collapse(text.ToString());
    }

    /// <summary>Whitespace runs as one space, without leading or trailing space.</summary>
    public static string Collapse(string text) => Whitespace().Replace(text, " ").Trim(' ');

    /// <summary>
    /// <paramref name="text"/> collapsed and, when longer than <paramref name="max"/>, cut at the
    /// last space before <c>max - 1</c> with <c>…</c> added, so it is at most <paramref name="max"/>
    /// long. A single word longer than that is cut inside the word.
    /// </summary>
    public static string Clean(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);

        var collapsed = Collapse(text);
        if (collapsed.Length <= max)
        {
            return collapsed;
        }

        var limit = max - Ellipsis.Length;
        var space = collapsed.LastIndexOf(' ', limit);
        var cut = space > 0 ? space : limit;
        if (char.IsHighSurrogate(collapsed[cut - 1]))
        {
            cut--;
        }

        return collapsed[..cut].TrimEnd(' ') + Ellipsis;
    }

    private static void Append(JsonElement node, StringBuilder text)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type == "text")
        {
            text.Append(node.TryGetProperty("text", out var value) ? value.GetString() : null);
            return;
        }

        if (type == "hardBreak")
        {
            text.Append(' ');
            return;
        }

        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                Append(child, text);
            }

            text.Append(' ');
        }
    }

    // The same set as the admin's port: ASCII whitespace, no-break and the Unicode spaces.
    [GeneratedRegex("[\\t\\n\\v\\f\\r \\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000\\ufeff]+")]
    private static partial Regex Whitespace();
}
