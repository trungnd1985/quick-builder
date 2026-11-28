using System.Text;
using System.Text.Json;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Rendering.Blocks;

/// <summary>
/// The border every block shares (066 research BB-04, contracts/block-border.md §3): classes for
/// the fixed widths and styles, and token-only custom properties for the colour, the corners and
/// the inner spacing.
/// </summary>
/// <remarks>
/// <para>
/// Every renderer appends <see cref="Classes"/> after its own classes and
/// <see cref="Declarations"/> to its own <c>context.Style</c> call; none writes border CSS of its
/// own. <c>useBorder.ts</c> produces the same output on the canvas, and
/// <c>BorderStyleTests</c> and <c>useBorder.spec.ts</c> hold both to the same cases.
/// </para>
/// <para>
/// No value from the document reaches CSS except an enum value as a class name or a token
/// variable (FR-020). Defaults come from <c>$defs/border</c> in the schema; <c>borderBox</c>
/// has the same ones, without padding.
/// </para>
/// </remarks>
internal static class BorderStyle
{
    private const string Definition = "border";
    private static readonly string[] Widths = ["none", "thin", "medium", "thick"];
    private static readonly string[] Styles = ["solid", "dashed", "dotted"];

    /// <summary>
    /// The classes to append to a block's class list, with a leading space, or an empty string
    /// when the block has no border.
    /// </summary>
    public static string Classes(JsonElement node, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Of(node) is not { } border)
        {
            return string.Empty;
        }

        var classes = new StringBuilder(" qb-border");
        var width = Choice(border, "width", Widths, context);
        if (width != "none")
        {
            classes.Append(" qb-border--").Append(width);
            classes.Append(" qb-border--").Append(Choice(border, "style", Styles, context));
        }

        if (Radius(border, context) is not null)
        {
            classes.Append(" qb-border--radius");
        }

        if (Padding(border, context) is not null)
        {
            classes.Append(" qb-border--pad");
        }

        return classes.ToString();
    }

    /// <summary>The custom properties to add to the block's per-node style, in contract order.</summary>
    public static (string Property, string Value)[] Declarations(JsonElement node, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Of(node) is not { } border)
        {
            return [];
        }

        var declarations = new List<(string, string)>(3);
        if (Choice(border, "width", Widths, context) != "none")
        {
            var color = border.TryGetProperty("color", out var own) ? own : (JsonElement?)null;
            declarations.Add(("--qb-border-color", context.Tokens.Value(color, TokenKind.Color, context.Defaults.OfDefinition(Definition, "color"))));
        }

        if (Radius(border, context) is { } radius)
        {
            declarations.Add(("--qb-border-radius", radius));
        }

        if (Padding(border, context) is { } padding)
        {
            declarations.Add(("--qb-border-pad", padding));
        }

        return [.. declarations];
    }

    /// <summary>
    /// A block's own declarations followed by its border's, for its one <c>context.Style</c> call.
    /// </summary>
    public static (string Property, string Value)[] With(JsonElement node, RenderContext context, params (string Property, string Value)[] own) =>
        [.. own, .. Declarations(node, context)];

    /// <summary>Whether the block has a border object at all: the two optional wrappers depend on it (BB-05).</summary>
    public static bool Has(JsonElement node) => Of(node) is not null;

    /// <summary>The block's own <c>border</c> object; a value of any other kind counts as absent.</summary>
    private static JsonElement? Of(JsonElement node) =>
        node.TryGetProperty("props", out var props)
        && props.ValueKind == JsonValueKind.Object
        && props.TryGetProperty("border", out var border)
        && border.ValueKind == JsonValueKind.Object
            ? border
            : null;

    // An enum value becomes a class name, so only an allowed one passes: the value, else the
    // schema default, else the first allowed (RenderContext.Choice).
    private static string Choice(JsonElement border, string name, string[] allowed, RenderContext context)
    {
        if (border.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Array.IndexOf(allowed, value.GetString()) >= 0)
        {
            return value.GetString()!;
        }

        return context.Defaults.OfDefinition(Definition, name) is { ValueKind: JsonValueKind.String } fallback && Array.IndexOf(allowed, fallback.GetString()) >= 0
            ? fallback.GetString()!
            : allowed[0];
    }

    // No default: absent, or naming a token the design lacks, the block keeps its own (BB-02).
    private static string? Radius(JsonElement border, RenderContext context) =>
        border.TryGetProperty("radius", out var radius) ? context.Tokens.ValueIfSet(radius, TokenKind.Radius) : null;

    private static string? Padding(JsonElement border, RenderContext context) =>
        border.TryGetProperty("padding", out var padding) ? context.Tokens.ValueIfSet(padding, TokenKind.Space) : null;
}
