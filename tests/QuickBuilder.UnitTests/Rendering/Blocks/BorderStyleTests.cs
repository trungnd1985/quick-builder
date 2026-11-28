using System.Text.Json;
using QuickBuilder.Rendering.Rendering.Blocks;

namespace QuickBuilder.UnitTests.Rendering.Blocks;

/// <summary>
/// The border every block shares, as classes and per-node values (066 research BB-04,
/// contracts/block-border.md §3). <c>useBorder.spec.ts</c> holds the canvas to the same cases.
/// </summary>
public sealed class BorderStyleTests
{
    private readonly RenderHarness _harness = new();

    private (string Classes, string Declarations) Of(string props)
    {
        using var node = JsonDocument.Parse($$$"""{"id":"b","type":"text","props":{{{props}}},"children":[]}""");
        var context = _harness.NewContext();
        var declarations = BorderStyle.Declarations(node.RootElement, context);
        return (BorderStyle.Classes(node.RootElement, context), string.Join(';', declarations.Select(d => $"{d.Property}:{d.Value}")));
    }

    [Fact]
    public void A_block_without_a_border_gets_nothing()
    {
        Assert.Equal((string.Empty, string.Empty), Of("{}"));
    }

    [Theory]
    [InlineData("thin", "solid")]
    [InlineData("thin", "dashed")]
    [InlineData("medium", "dotted")]
    [InlineData("thick", "solid")]
    public void A_width_and_style_become_classes_and_the_colour_a_value(string width, string style)
    {
        var (classes, declarations) = Of("{\"border\":{\"width\":\"" + width + "\",\"style\":\"" + style + "\",\"color\":{\"token\":\"color-brand\"}}}");

        Assert.Equal($" qb-border qb-border--{width} qb-border--{style}", classes);
        Assert.Equal("--qb-border-color:var(--qb-color-color-brand)", declarations);
    }

    [Fact]
    public void Style_and_colour_default_from_the_schema()
    {
        var (classes, declarations) = Of("""{"border":{"width":"thin"}}""");

        Assert.Equal(" qb-border qb-border--thin qb-border--solid", classes);
        Assert.Equal("--qb-border-color:var(--qb-color-color-border)", declarations);
    }

    [Fact]
    public void Width_none_draws_no_line_and_writes_no_colour()
    {
        var (classes, declarations) = Of("""{"border":{"width":"none","style":"dashed","color":{"token":"color-brand"}}}""");

        Assert.Equal(" qb-border", classes);
        Assert.Equal(string.Empty, declarations);
    }

    [Fact]
    public void A_colour_token_that_is_gone_falls_back_to_the_default()
    {
        var (_, declarations) = Of("""{"border":{"width":"thin","color":{"token":"color-gone"}}}""");

        Assert.Equal("--qb-border-color:var(--qb-color-color-border)", declarations);
    }

    [Fact]
    public void A_radius_that_exists_rounds_the_corners_even_without_a_line()
    {
        var (classes, declarations) = Of("""{"border":{"radius":{"token":"radius-md"}}}""");

        Assert.Equal(" qb-border qb-border--radius", classes);
        Assert.Equal("--qb-border-radius:var(--qb-radius-radius-md)", declarations);
    }

    [Fact]
    public void A_radius_that_is_gone_leaves_the_corners_alone()
    {
        Assert.Equal((" qb-border", string.Empty), Of("""{"border":{"radius":{"token":"radius-gone"}}}"""));
    }

    [Fact]
    public void Padding_that_exists_moves_the_line_off_the_content()
    {
        var (classes, declarations) = Of("""{"border":{"width":"thin","padding":{"token":"space-sm"}}}""");

        Assert.Equal(" qb-border qb-border--thin qb-border--solid qb-border--pad", classes);
        Assert.Equal("--qb-border-color:var(--qb-color-color-border);--qb-border-pad:var(--qb-space-space-sm)", declarations);
    }

    [Fact]
    public void Padding_that_is_gone_leaves_the_spacing_alone()
    {
        Assert.Equal((" qb-border", string.Empty), Of("""{"border":{"padding":{"token":"space-gone"}}}"""));
    }

    [Fact]
    public void Every_part_together_comes_in_contract_order()
    {
        var (classes, declarations) = Of("""
            {"border":{"width":"medium","style":"dotted","color":{"token":"color-text"},"radius":{"token":"radius-lg"},"padding":{"token":"space-md"}}}
            """);

        Assert.Equal(" qb-border qb-border--medium qb-border--dotted qb-border--radius qb-border--pad", classes);
        Assert.Equal("--qb-border-color:var(--qb-color-color-text);--qb-border-radius:var(--qb-radius-radius-lg);--qb-border-pad:var(--qb-space-space-md)", declarations);
    }

    [Theory]
    [InlineData("\"thin\"")]
    [InlineData("[]")]
    [InlineData("null")]
    public void A_border_that_is_not_an_object_is_treated_as_absent(string value)
    {
        Assert.Equal((string.Empty, string.Empty), Of("{\"border\":" + value + "}"));
    }

    [Fact]
    public void Values_outside_the_enums_never_reach_a_class_name()
    {
        var (classes, _) = Of("""{"border":{"width":"\"><script>","style":"\"><b>"}}""");

        Assert.Equal(" qb-border", classes);
    }
}
