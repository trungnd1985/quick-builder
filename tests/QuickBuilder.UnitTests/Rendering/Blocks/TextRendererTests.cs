using static QuickBuilder.UnitTests.Rendering.RenderHarness;

namespace QuickBuilder.UnitTests.Rendering.Blocks;

/// <summary>Heading, Text and Rich Text as published (012 FR-023, FR-025, FR-026, research PR-11).</summary>
public sealed class TextRendererTests
{
    private readonly RenderHarness _harness = new();

    [Fact]
    public void A_heading_is_its_level_with_the_size_of_its_level()
    {
        var (html, rules) = _harness.Block("""{"id":"h","type":"heading","props":{"text":"About us","level":3,"align":"center"},"children":[]}""");

        Assert.Equal("""<h3 class="qb-heading qb-align--center" data-qb="p.h">About us</h3>""", html);
        Assert.Contains("--qb-heading-size:var(--qb-text-text-xl)", rules, StringComparison.Ordinal);
        Assert.Contains("--qb-heading-color:var(--qb-color-color-text)", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_without_a_level_is_level_two()
    {
        Assert.StartsWith("<h2 ", _harness.Block("""{"id":"h","type":"heading","props":{"text":"x"},"children":[]}""").Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_encoded_and_keeps_its_line_breaks()
    {
        var (html, _) = _harness.Block("""{"id":"t","type":"text","props":{"text":"Line one\n<script>alert(1)</script> & two"},"children":[]}""");

        Assert.Equal("<p class=\"qb-text qb-align--start\" data-qb=\"p.t\">Line one\n&lt;script&gt;alert(1)&lt;/script&gt; &amp; two</p>", html);
    }

    [Fact]
    public void Rich_text_uses_only_the_allowed_elements()
    {
        var (html, _) = _harness.Block($$$"""
            {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[
              {"type":"paragraph","content":[
                {"type":"text","text":"Plain "},
                {"type":"text","text":"bold","marks":[{"type":"bold"}]},
                {"type":"hardBreak"},
                {"type":"text","text":"both","marks":[{"type":"bold"},{"type":"italic"}]}]},
              {"type":"orderedList","attrs":{"start":3},"content":[
                {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"three"}]},
                  {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"nested"}]}]}]}]}]}]}},
             "children":[]}
            """);

        Assert.Equal(
            "<div class=\"qb-rich-text qb-align--start\" data-qb=\"p.r\"><div class=\"qb-rich-text__body\">"
            + "<p><span>Plain </span><strong>bold</strong><br><em><strong>both</strong></em></p>"
            + "<ol start=\"3\"><li><p><span>three</span></p><ul><li><p><span>nested</span></p></li></ul></li></ol>"
            + "</div></div>",
            html);
    }

    [Fact]
    public void A_rich_text_link_to_a_live_page_uses_its_current_address_under_the_base_path()
    {
        _harness.BasePath = "/site";

        var (html, _) = _harness.Block(RichTextLink($$$"""{"type":"page","pageId":"{{{LivePage}}}"}"""));

        // Marked text has no span of its own, as on the canvas (RichTextView.ts).
        Assert.Contains("<a class=\"qb-link\" href=\"/site/contact\">go</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rich_text_link_to_a_page_that_is_not_live_keeps_only_its_text()
    {
        var (html, _) = _harness.Block(RichTextLink($$$"""{"type":"page","pageId":"{{{DraftOnlyPage}}}"}"""));

        Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
        Assert.Contains("<span>go</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_rich_text_link_keeps_its_address_and_a_disallowed_one_is_dropped()
    {
        Assert.Contains("href=\"https://example.com/x\"", _harness.Block(RichTextLink("""{"type":"url","href":"https://example.com/x"}""")).Html, StringComparison.Ordinal);
        Assert.DoesNotContain("href", _harness.Block(RichTextLink("""{"type":"url","href":"javascript:alert(1)"}""")).Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_rich_text_nodes_are_skipped()
    {
        var (html, _) = _harness.Block("""
            {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[
              {"type":"blockquote","content":[{"type":"text","text":"never"}]},
              {"type":"paragraph","content":[{"type":"image","attrs":{"src":"x"}},{"type":"text","text":"kept"}]}]}},"children":[]}
            """);

        Assert.DoesNotContain("never", html, StringComparison.Ordinal);
        Assert.DoesNotContain("img", html, StringComparison.Ordinal);
        Assert.Contains("<span>kept</span>", html, StringComparison.Ordinal);
    }

    private static string RichTextLink(string to) => $$$"""
        {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[
          {"type":"paragraph","content":[{"type":"text","text":"go","marks":[{"type":"link","attrs":{"to":{{{to}}}}}]}]}]}},"children":[]}
        """;
}
