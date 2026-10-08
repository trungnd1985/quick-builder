using System.Text.Json;

namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// What a rich text document (<c>$defs/rtDoc</c>) must also meet beyond the schema, and what
/// the write paths need to know about one (page-schema.md §3 rules 10–12; 035 research RT-02,
/// RT-10, RT-13).
/// </summary>
/// <remarks>
/// Rich text lives in Rich Text blocks of page and component documents and in rich text item
/// values (014), so the rules sit here, beside the schema, for both modules. Every method
/// accepts any JSON: the rules run after the schema, but the questions are also asked of
/// stored documents, and anything that is not a document simply has nothing to report.
/// </remarks>
public static class RichTextRules
{
    /// <summary>The request header an admin sends to say which rich text formats it understands.</summary>
    public const string ClientHeader = "X-QB-Rich-Text";

    /// <summary>The level of an admin that understands every 035 format.</summary>
    public const int CurrentClientLevel = 2;

    /// <summary>A rule a document breaks, and where: an RFC 6901 pointer.</summary>
    public sealed record Failure(string Rule, string Location);

    private static readonly HashSet<string> ExtendedNodes = new(StringComparer.Ordinal) { "heading", "blockquote", "image", "table" };

    private static readonly HashSet<string> ExtendedMarks = new(StringComparer.Ordinal) { "underline", "strike", "code" };

    /// <summary>
    /// The first of rules 10 (image and table counts), 11 (rectangular tables) and 12 (header
    /// cells in the first row only) that <paramref name="document"/> breaks, in that order; null
    /// when it meets them all. <paramref name="location"/> is where the document sits.
    /// </summary>
    public static Failure? Check(JsonElement document, string location)
    {
        var tables = new List<(JsonElement Table, string Pointer)>();
        var images = 0;

        foreach (var (node, at) in Nodes(document, location))
        {
            switch (TypeOf(node))
            {
                case "image":
                    images++;
                    break;
                case "table":
                    tables.Add((node, at));
                    break;
            }
        }

        if (images > PageSchema.MaxRichTextImages)
        {
            return new Failure("rich_text_too_many_images", location);
        }

        if (tables.Count > PageSchema.MaxRichTextTables)
        {
            return new Failure("rich_text_too_many_tables", location);
        }

        foreach (var (table, at) in tables)
        {
            var rows = Content(table);
            for (var r = 1; r < rows.Count; r++)
            {
                if (Content(rows[r]).Count != Content(rows[0]).Count)
                {
                    return new Failure("table_not_rectangular", $"{at}/content/{r}");
                }
            }
        }

        foreach (var (table, at) in tables)
        {
            var rows = Content(table);
            for (var r = 0; r < rows.Count; r++)
            {
                var cells = Content(rows[r]);
                var headerRow = r == 0 && cells.Any(c => TypeOf(c) == "tableHeader");
                for (var c = 0; c < cells.Count; c++)
                {
                    if ((TypeOf(cells[c]) == "tableHeader") != headerRow)
                    {
                        return new Failure("table_header_misplaced", $"{at}/content/{r}/content/{c}");
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The images that may not be published as they are: neither alternative text that is not
    /// blank, nor marked decorative (035 FR-014). Pointers are below <paramref name="location"/>.
    /// </summary>
    public static IReadOnlyList<string> ImagesWithoutAlt(JsonElement document, string location) =>
        [.. Nodes(document, location)
            .Where(n => TypeOf(n.Node) == "image" && !HasAltOrIsDecorative(n.Node))
            .Select(n => n.Pointer)];

    /// <summary>
    /// Whether <paramref name="document"/> uses anything an admin from before 035 cannot keep: a
    /// heading, quote, image or table, an alignment, or an underline, strikethrough or code mark.
    /// </summary>
    public static bool UsesExtendedFormats(JsonElement document) =>
        Nodes(document, string.Empty).Any(n =>
            ExtendedNodes.Contains(TypeOf(n.Node) ?? string.Empty)
            || (n.Node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("align", out _))
            || Marks(n.Node).Any(m => ExtendedMarks.Contains(TypeOf(m) ?? string.Empty)));

    /// <summary>
    /// Whether <paramref name="document"/> is empty: no text and no images (035 spec edge case
    /// "Empty structures"). Empty paragraphs, headings, quotes and tables don't count, so an
    /// empty rich text is no value, and one holding only an image is not empty.
    /// </summary>
    public static bool IsEmpty(JsonElement document) =>
        !Nodes(document, string.Empty).Any(n => TypeOf(n.Node) switch
        {
            "image" => true,
            "text" => n.Node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && text.GetString()!.Length > 0,
            _ => false,
        });

    private static bool HasAltOrIsDecorative(JsonElement image)
    {
        if (!image.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (attrs.TryGetProperty("decorative", out var decorative) && decorative.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return attrs.TryGetProperty("alt", out var alt) && alt.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(alt.GetString());
    }

    /// <summary>Every node object below the document, with its pointer, in document order.</summary>
    private static IEnumerable<(JsonElement Node, string Pointer)> Nodes(JsonElement document, string pointer)
    {
        if (document.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        var pending = new Stack<(JsonElement Node, string Pointer)>();
        Push(pending, document, pointer);

        while (pending.Count > 0)
        {
            var (node, at) = pending.Pop();
            yield return (node, at);
            Push(pending, node, at);
        }
    }

    private static void Push(Stack<(JsonElement, string)> pending, JsonElement node, string pointer)
    {
        var children = Content(node);
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].ValueKind == JsonValueKind.Object)
            {
                pending.Push((children[i], $"{pointer}/content/{i}"));
            }
        }
    }

    private static List<JsonElement> Content(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? [.. content.EnumerateArray()]
            : [];

    private static IEnumerable<JsonElement> Marks(JsonElement node) =>
        node.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array
            ? marks.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object)
            : [];

    private static string? TypeOf(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;
}
