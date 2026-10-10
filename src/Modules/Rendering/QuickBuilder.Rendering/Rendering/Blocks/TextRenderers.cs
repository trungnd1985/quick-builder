using System.Text.Json;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Blocks;

// The text blocks. Each matches its canvas component in src/admin/src/editor/blocks/<type>/.

/// <summary>A heading, levels 1 to 6; its size follows its level (HeadingBlock.vue).</summary>
internal sealed class HeadingRenderer : IBlockRenderer
{
    private static readonly string[] Alignments = ["start", "center", "end"];

    // SIZE_BY_LEVEL in HeadingBlock.vue.
    private static readonly string[] SizeByLevel = ["text-3xl", "text-2xl", "text-xl", "text-lg", "text-md", "text-md"];

    private static readonly JsonElement MediumSize = JsonDocument.Parse("""{"token":"text-md"}""").RootElement.Clone();

    public string Type => "heading";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var level = Math.Clamp(context.Int(node, "level", 2), 1, 6);
        var tag = $"h{level}";
        using var size = JsonDocument.Parse($$"""{"token":"{{SizeByLevel[level - 1]}}"}""");

        writer.Open(tag).Attribute("class", $"qb-heading qb-align--{context.Choice(node, "align", Alignments)}");
        context.Style(writer, scope, node,
            ("--qb-heading-color", context.Token(node, "color", TokenKind.Color)),
            ("--qb-heading-size", context.Tokens.Value(size.RootElement, TokenKind.Text, MediumSize)));
        writer.EndTag().Text(context.BoundText(node, "text")).Close(tag);
    }
}

/// <summary>Plain text, line breaks kept by the stylesheet (TextBlock.vue).</summary>
internal sealed class TextRenderer : IBlockRenderer
{
    private static readonly string[] Alignments = ["start", "center", "end"];

    public string Type => "text";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Open("p").Attribute("class", $"qb-text qb-align--{context.Choice(node, "align", Alignments)}");
        context.Style(writer, scope, node,
            ("--qb-text-color", context.Token(node, "color", TokenKind.Color)),
            ("--qb-text-size", context.Token(node, "size", TokenKind.Text)));
        writer.EndTag().Text(context.BoundText(node, "text")).Close("p");
    }
}

/// <summary>Formatted text (RichTextBlock.vue, RichTextView.ts).</summary>
internal sealed class RichTextBlockRenderer : IBlockRenderer
{
    private static readonly string[] Alignments = ["start", "center", "end"];

    public string Type => "rich-text";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Open("div").Attribute("class", $"qb-rich-text qb-align--{context.Choice(node, "align", Alignments)}");
        context.Style(writer, scope, node, ("--qb-text-color", context.Token(node, "color", TokenKind.Color)));
        writer.EndTag();
        var bound = context.Bound(node, "content");
        RichTextRenderer.Render(bound is { State: BoundState.Value, RichText: { } content } ? content : context.Prop(node, "content"), context, writer);
        writer.Close("div");
    }
}
