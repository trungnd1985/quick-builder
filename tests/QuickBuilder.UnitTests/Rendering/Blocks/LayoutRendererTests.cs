namespace QuickBuilder.UnitTests.Rendering.Blocks;

/// <summary>Section, Container, Spacer and Divider as published (012 FR-023, page-schema.md §9).</summary>
public sealed class LayoutRendererTests
{
    private readonly RenderHarness _harness = new();

    [Fact]
    public void A_section_uses_its_defaults_when_nothing_is_set()
    {
        var (html, rules) = _harness.Block("""{"id":"s","type":"section","props":{},"children":[]}""");

        Assert.Equal("""<section class="qb-section qb-section--contained" data-qb="p.s"><div class="qb-section__inner"></div></section>""", html);
        Assert.Equal("""[data-qb="p.s"]{--qb-section-bg:transparent;--qb-section-pad:var(--qb-space-space-xl);}""", rules);
    }

    [Fact]
    public void A_section_holds_its_children_inside_its_inner_box()
    {
        var (html, _) = _harness.Block("""
            {"id":"s","type":"section","props":{"width":"full","background":{"token":"color-brand"}},"children":[
              {"id":"sp","type":"spacer","props":{},"children":[]}]}
            """);

        Assert.StartsWith("""<section class="qb-section qb-section--full" data-qb="p.s"><div class="qb-section__inner"><div class="qb-spacer" """, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_that_no_longer_exists_falls_back_to_the_default()
    {
        var (_, rules) = _harness.Block("""{"id":"s","type":"section","props":{"paddingY":{"token":"space-gone"}},"children":[]}""");

        Assert.Contains("--qb-section-pad:var(--qb-space-space-xl)", rules, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"direction":"vertical"}""", "qb-container qb-container--vertical qb-align--start qb-container--max-full qb-container--stack-on-mobile")]
    [InlineData("""{"direction":"horizontal","columns":"2-1","align":"center","maxWidth":"narrow","stackOnMobile":false}""", "qb-container qb-container--horizontal qb-align--center qb-container--max-narrow qb-container--grid qb-columns--2-1")]
    [InlineData("""{"direction":"horizontal","columns":"equal"}""", "qb-container qb-container--horizontal qb-align--start qb-container--max-full qb-container--stack-on-mobile")]
    public void A_container_carries_the_canvas_classes(string props, string classes)
    {
        var (html, _) = _harness.Block($$$"""{"id":"c","type":"container","props":{{{props}}},"children":[]}""");

        Assert.StartsWith($"<div class=\"{classes}\" data-qb=\"p.c\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_outside_its_enum_never_reaches_a_class_name()
    {
        var (html, _) = _harness.Block("""{"id":"c","type":"container","props":{"direction":"\"><script>"},"children":[]}""");

        Assert.Contains("qb-container--vertical", html, StringComparison.Ordinal);
        Assert.DoesNotContain("script", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Spacer_and_divider()
    {
        Assert.Equal("""<div class="qb-spacer" data-qb="p.x"></div>""", _harness.Block("""{"id":"x","type":"spacer","props":{},"children":[]}""").Html);

        var (html, rules) = _harness.Block("""{"id":"d","type":"divider","props":{"thickness":"thick"},"children":[]}""");
        Assert.Equal("""<hr class="qb-divider qb-divider--thick" data-qb="p.d">""", html);
        Assert.Contains("--qb-divider-color:var(--qb-color-color-border)", rules, StringComparison.Ordinal);
    }
}
