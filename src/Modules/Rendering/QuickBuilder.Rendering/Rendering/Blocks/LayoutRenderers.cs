using System.Text.Json;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Blocks;

// The layout blocks. Each matches its canvas component in src/admin/src/editor/blocks/<type>/
// (page-schema.md §9): same element, same classes, same custom properties.

/// <summary>A full-width band holding the page's content (011 FR-012, FR-013).</summary>
internal sealed class SectionRenderer : IBlockRenderer
{
    private static readonly string[] Widths = ["narrow", "contained", "wide", "full"];

    public string Type => "section";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Open("section").Attribute("class", $"qb-section qb-section--{context.Choice(node, "width", Widths)}");
        context.Style(writer, scope, node,
            ("--qb-section-bg", context.Token(node, "background", TokenKind.Color, "transparent")),
            ("--qb-section-pad", context.Token(node, "paddingY", TokenKind.Space)));
        writer.EndTag().Markup("<div class=\"qb-section__inner\">");
        context.RenderChildren(node, scope, writer);
        writer.Markup("</div>").Close("section");
    }
}

/// <summary>A group of blocks in a column or a row (011 FR-013).</summary>
internal sealed class ContainerRenderer : IBlockRenderer
{
    private static readonly string[] Directions = ["vertical", "horizontal"];
    private static readonly string[] Alignments = ["start", "center", "end"];
    private static readonly string[] MaxWidths = ["narrow", "normal", "wide", "full"];
    private static readonly string[] Columns = ["equal", "2-1", "1-2", "3-1", "1-3", "2-1-1"];

    public string Type => "container";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        var direction = context.Choice(node, "direction", Directions);
        var columns = context.Choice(node, "columns", Columns);

        // The canvas component's class order: direction, alignment, width, then the optional ones.
        var classes = new List<string>
        {
            "qb-container",
            $"qb-container--{direction}",
            $"qb-align--{context.Choice(node, "align", Alignments)}",
            $"qb-container--max-{context.Choice(node, "maxWidth", MaxWidths)}",
        };

        if (context.Bool(node, "stackOnMobile"))
        {
            classes.Add("qb-container--stack-on-mobile");
        }

        if (direction == "horizontal" && columns != "equal")
        {
            classes.Add("qb-container--grid");
            classes.Add($"qb-columns--{columns}");
        }

        writer.Open("div").Attribute("class", string.Join(' ', classes));
        context.Style(writer, scope, node,
            ("--qb-container-gap", context.Token(node, "gap", TokenKind.Space)),
            ("--qb-container-pad", context.Token(node, "padding", TokenKind.Space)),
            ("--qb-container-bg", context.Token(node, "background", TokenKind.Color, "transparent")));
        writer.EndTag();
        context.RenderChildren(node, scope, writer);
        writer.Close("div");
    }
}

/// <summary>Empty vertical space from the spacing scale.</summary>
internal sealed class SpacerRenderer : IBlockRenderer
{
    public string Type => "spacer";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Open("div").Attribute("class", "qb-spacer");
        context.Style(writer, scope, node, ("--qb-spacer-height", context.Token(node, "height", TokenKind.Space)));
        writer.EndTag().Close("div");
    }
}

/// <summary>A horizontal rule.</summary>
internal sealed class DividerRenderer : IBlockRenderer
{
    private static readonly string[] Thicknesses = ["thin", "thick"];

    public string Type => "divider";

    public void Render(JsonElement node, string scope, RenderContext context, HtmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Open("hr").Attribute("class", $"qb-divider qb-divider--{context.Choice(node, "thickness", Thicknesses)}");
        context.Style(writer, scope, node,
            ("--qb-divider-color", context.Token(node, "color", TokenKind.Color)),
            ("--qb-divider-spacing", context.Token(node, "spacing", TokenKind.Space)));
        writer.EndTag();
    }
}
