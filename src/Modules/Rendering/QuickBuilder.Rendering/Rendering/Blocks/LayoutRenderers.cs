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

        var width = context.Choice(node, "width", Widths);
        writer.Open("section").Attribute("class", $"qb-section qb-section--{width}{BorderStyle.Classes(node, context)}");
        context.Style(writer, scope, node, BorderStyle.With(node, context,
            ("--qb-section-bg", context.Token(node, "background", TokenKind.Color, "transparent")),
            ("--qb-section-pad", context.Token(node, "paddingY", TokenKind.Space))));
        writer.EndTag().Markup("<div class=\"qb-section__inner\">");

        // 013 MV-15: the width images inside may take, for their sizes hint (blocks.css).
        var outer = context.SectionWidth;
        var content = context.Tokens.ContentWidths;
        context.SectionWidth = width switch
        {
            "narrow" => content.Narrow,
            "wide" => content.Wide,
            "full" => null,
            _ => content.Normal,
        };

        context.RenderChildren(node, scope, writer);
        context.SectionWidth = outer;
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

        writer.Open("div").Attribute("class", string.Join(' ', classes) + BorderStyle.Classes(node, context));
        context.Style(writer, scope, node, BorderStyle.With(node, context,
            ("--qb-container-gap", context.Token(node, "gap", TokenKind.Space)),
            ("--qb-container-pad", context.Token(node, "padding", TokenKind.Space)),
            ("--qb-container-bg", context.Token(node, "background", TokenKind.Color, "transparent"))));
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

        writer.Open("div").Attribute("class", "qb-spacer" + BorderStyle.Classes(node, context));
        context.Style(writer, scope, node, BorderStyle.With(node, context, ("--qb-spacer-height", context.Token(node, "height", TokenKind.Space))));
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

        var own = new[]
        {
            ("--qb-divider-color", context.Token(node, "color", TokenKind.Color)),
            ("--qb-divider-spacing", context.Token(node, "spacing", TokenKind.Space)),
        };
        var rule = $"qb-divider qb-divider--{context.Choice(node, "thickness", Thicknesses)}";

        // 066 BB-05: the line is the hr's own top border, so a border frames a wrapper around it,
        // drawn only when the block has one. The wrapper carries every value; the hr inherits them.
        if (BorderStyle.Has(node))
        {
            writer.Open("div").Attribute("class", "qb-divider-frame" + BorderStyle.Classes(node, context));
            context.Style(writer, scope, node, BorderStyle.With(node, context, own));
            writer.EndTag().Open("hr").Attribute("class", rule).EndTag().Close("div");
            return;
        }

        writer.Open("hr").Attribute("class", rule);
        context.Style(writer, scope, node, own);
        writer.EndTag();
    }
}
