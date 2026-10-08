using System.Text.Json;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// Stored rich text as HTML, through an allow-list (012 research PR-11; constitution Principle IX).
/// </summary>
/// <remarks>
/// The same elements the canvas draws (RichTextView.ts): paragraph → <c>p</c>, heading →
/// <c>h2</c>–<c>h4</c> and quote → <c>blockquote</c> (035), bullet list → <c>ul</c>, ordered list →
/// <c>ol</c>, list item → <c>li</c>, hard break → <c>br</c>, and the marks bold → <c>strong</c>,
/// italic → <c>em</c>, underline → <c>u</c>, strike → <c>del</c>, code → <c>code</c> (035), link → <c>a</c>. Nothing else is ever
/// written; a node or mark of another kind is skipped. Unmarked text sits in a <c>span</c>, as
/// on the canvas. A link to a page that is not live keeps its text and loses the link.
/// </remarks>
internal static class RichTextRenderer
{
    public static void Render(JsonElement? document, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Markup("<div class=\"qb-rich-text__body\">");

        if (document is { ValueKind: JsonValueKind.Object } doc && doc.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                RenderBlock(block, context, writer);
            }
        }

        writer.Markup("</div>");
    }

    private static void RenderBlock(JsonElement node, RenderContext context, HtmlWriter writer)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("type", out var type))
        {
            return;
        }

        switch (type.GetString())
        {
            case "paragraph":
                writer.Markup($"<p{AlignClass(node)}>");
                foreach (var inline in Children(node))
                {
                    RenderInline(inline, context, writer);
                }

                writer.Markup("</p>");
                break;

            case "heading":
                var level = HeadingLevel(node);
                writer.Markup($"<h{level}{AlignClass(node)}>");
                foreach (var inline in Children(node))
                {
                    RenderInline(inline, context, writer);
                }

                writer.Markup($"</h{level}>");
                break;

            case "blockquote":
                writer.Markup("<blockquote>");
                foreach (var child in Children(node))
                {
                    RenderBlock(child, context, writer);
                }

                writer.Markup("</blockquote>");
                break;

            case "image":
                RenderImage(node, context, writer);
                break;

            case "table":
                RenderTable(node, context, writer);
                break;

            case "bulletList":
                RenderList("ul", node, context, writer, start: null);
                break;

            case "orderedList":
                var start = node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
                    && attrs.TryGetProperty("start", out var s) && s.TryGetInt32(out var number)
                        ? number
                        : (int?)null;
                RenderList("ol", node, context, writer, start);
                break;

            case "listItem":
                writer.Markup("<li>");
                foreach (var child in Children(node))
                {
                    RenderBlock(child, context, writer);
                }

                writer.Markup("</li>");
                break;
        }
    }

    private static void RenderList(string tag, JsonElement node, RenderContext context, HtmlWriter writer, int? start)
    {
        writer.Open(tag);
        if (start is { } first)
        {
            writer.Attribute("start", first.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        writer.EndTag();
        foreach (var item in Children(node))
        {
            RenderBlock(item, context, writer);
        }

        writer.Close(tag);
    }

    private static void RenderInline(JsonElement node, RenderContext context, HtmlWriter writer)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("type", out var type))
        {
            return;
        }

        if (type.GetString() == "hardBreak")
        {
            writer.Markup("<br>");
            return;
        }

        if (type.GetString() != "text" || !node.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String)
        {
            return;
        }

        // Marks wrap the text in the order they are listed: the first innermost, as on the canvas.
        var content = new HtmlWriter().Text(textValue.GetString()).ToString();
        var wrapped = false;

        if (node.TryGetProperty("marks", out var marks) && marks.ValueKind == JsonValueKind.Array)
        {
            foreach (var mark in marks.EnumerateArray())
            {
                var markType = mark.ValueKind == JsonValueKind.Object && mark.TryGetProperty("type", out var t) ? t.GetString() : null;

                switch (markType)
                {
                    case "bold":
                        content = $"<strong>{content}</strong>";
                        wrapped = true;
                        break;
                    case "italic":
                        content = $"<em>{content}</em>";
                        wrapped = true;
                        break;
                    case "underline":
                        content = $"<u>{content}</u>";
                        wrapped = true;
                        break;
                    case "strike":
                        // Struck-through text is deleted text, and is announced as such (035 US2).
                        content = $"<del>{content}</del>";
                        wrapped = true;
                        break;
                    case "code":
                        content = $"<code>{content}</code>";
                        wrapped = true;
                        break;
                    case "link":
                        var to = mark.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("to", out var target)
                            ? target
                            : (JsonElement?)null;

                        if (LinkResolver.Resolve(to, context) is { } link && LinkResolver.IsWritable(link))
                        {
                            var anchor = new HtmlWriter().Open("a").Attribute("class", "qb-link");
                            LinkResolver.Write(anchor, link, newTab: false);
                            content = $"{anchor.EndTag()}{content}</a>";
                            wrapped = true;
                        }

                        break;
                }
            }
        }

        // Both pieces were produced by HtmlWriter: the text encoded, the tags constant.
        writer.Markup(wrapped ? content : $"<span>{content}</span>");
    }

    /// <summary>
    /// A rich text image (035 FR-015): the Image block's pipeline at the text's width, never
    /// wider than the file itself. A file that is gone, or another site's, shows nothing (FR-017).
    /// </summary>
    private static void RenderImage(JsonElement node, RenderContext context, HtmlWriter writer)
    {
        if (!node.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object
            || !attrs.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object
            || !media.TryGetProperty("mediaId", out var id) || id.ValueKind != JsonValueKind.String
            || !Guid.TryParse(id.GetString(), out var mediaId)
            || !context.Inputs.Media.TryGetValue(mediaId, out var file))
        {
            return;
        }

        var decorative = attrs.TryGetProperty("decorative", out var flag) && flag.ValueKind == JsonValueKind.True;
        var alt = !decorative && attrs.TryGetProperty("alt", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : string.Empty;
        var basePath = PageShell.TrimBase(context.Inputs.BasePath);
        var source = file.Variants.LastOrDefault(v => v.Width <= Blocks.ImageRenderer.DefaultSourceWidth)?.PublicPath ?? file.PublicPath;

        writer.Open("figure").Attribute("class", "qb-rt-image").EndTag();
        writer.Open("img");
        writer.SitePath("src", basePath + source);

        if (file.Variants.Count > 0)
        {
            writer.Attribute("srcset", Blocks.ImageRenderer.SourceSet(file, basePath));
            writer.Attribute("sizes", Blocks.ImageRenderer.Sizes(context.SectionWidth, file.Width));
        }

        if (file.Width is { } w && file.Height is { } h)
        {
            writer.Attribute("width", w.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Attribute("height", h.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        writer.Attribute("alt", alt).Attribute("loading", "lazy").Attribute("decoding", "async").EndTag();
        writer.Close("figure");
    }

    /// <summary>
    /// A table (035 FR-015): in an area that scrolls sideways, focusable so keyboards can scroll
    /// it; header cells, which only the first row has, are the headers of their columns.
    /// </summary>
    private static void RenderTable(JsonElement node, RenderContext context, HtmlWriter writer)
    {
        writer.Markup("<div class=\"qb-rt-table\" tabindex=\"0\"><table><tbody>");
        foreach (var row in Children(node))
        {
            writer.Markup("<tr>");
            foreach (var cell in Children(row))
            {
                var header = cell.TryGetProperty("type", out var type) && type.GetString() == "tableHeader";
                writer.Markup(header ? "<th scope=\"col\">" : "<td>");
                foreach (var block in Children(cell))
                {
                    RenderBlock(block, context, writer);
                }

                writer.Markup(header ? "</th>" : "</td>");
            }

            writer.Markup("</tr>");
        }

        writer.Markup("</tbody></table></div>");
    }

    /// <summary>
    /// A paragraph's or heading's own alignment, as one of the fixed classes blocks use (035
    /// FR-016): never an inline style, and nothing for a value the contract does not have.
    /// </summary>
    private static string AlignClass(JsonElement node) =>
        node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
            && attrs.TryGetProperty("align", out var align) && align.ValueKind == JsonValueKind.String
            && align.GetString() is "start" or "center" or "end"
            ? $" class=\"qb-align--{align.GetString()}\""
            : string.Empty;

    /// <summary>A heading's level, 2 to 4; anything else is drawn as the nearest of those (035).</summary>
    private static int HeadingLevel(JsonElement node) =>
        node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
            && attrs.TryGetProperty("level", out var level) && level.TryGetInt32(out var value)
            ? Math.Clamp(value, 2, 4)
            : 2;

    private static List<JsonElement> Children(JsonElement node) =>
        node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? [.. content.EnumerateArray()]
            : [];
}
