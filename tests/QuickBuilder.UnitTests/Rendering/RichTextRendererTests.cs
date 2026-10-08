namespace QuickBuilder.UnitTests.Rendering;

/// <summary>
/// The 035 rich text formats as published, through the renderer's allow-list (035 FR-015,
/// research RT-04; page-schema.md "Rendering"). The 015 formats are covered by
/// <c>TextRendererTests</c> and the golden files, which must not change (SC-001).
/// </summary>
public sealed class RichTextRendererTests
{
    private const string Start = "<div class=\"qb-rich-text qb-align--start\" data-qb=\"p.r\"><div class=\"qb-rich-text__body\">";
    private const string End = "</div></div>";

    private readonly RenderHarness _harness = new();

    /// <summary>The rich text body's HTML for the given top-level blocks.</summary>
    private string Body(string blocks)
    {
        var (html, _) = _harness.Block(
            """{"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[BLOCKS]}},"children":[]}"""
                .Replace("BLOCKS", blocks, StringComparison.Ordinal));

        Assert.StartsWith(Start, html, StringComparison.Ordinal);
        Assert.EndsWith(End, html, StringComparison.Ordinal);
        return html[Start.Length..^End.Length];
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_heading_is_its_stored_level(int level) =>
        Assert.Equal(
            $"<h{level}><span>Opening &lt;hours&gt;</span></h{level}>",
            Body($$"""{"type":"heading","attrs":{"level":{{level}}},"content":[{"type":"text","text":"Opening <hours>"}]}"""));

    [Fact]
    public void A_heading_keeps_its_marks_and_an_empty_one_is_empty() =>
        Assert.Equal(
            "<h3><span>Weekdays </span><strong>early</strong></h3><h2></h2>",
            Body("""
                {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Weekdays "},{"type":"text","text":"early","marks":[{"type":"bold"}]}]},
                {"type":"heading","attrs":{"level":2}}
                """));

    [Fact]
    public void A_heading_level_outside_two_to_four_is_drawn_as_the_nearest_allowed() =>
        Assert.Equal(
            "<h2><span>a</span></h2><h4><span>b</span></h4>",
            Body("""
                {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"a"}]},
                {"type":"heading","attrs":{"level":6},"content":[{"type":"text","text":"b"}]}
                """));

    [Fact]
    public void A_quote_holds_its_paragraphs_and_lists() =>
        Assert.Equal(
            "<blockquote><p><span>Best bread in town.</span></p><ul><li><p><span>Sourdough</span></p></li></ul></blockquote><blockquote><p></p></blockquote>",
            Body("""
                {"type":"blockquote","content":[
                  {"type":"paragraph","content":[{"type":"text","text":"Best bread in town."}]},
                  {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Sourdough"}]}]}]}]},
                {"type":"blockquote","content":[{"type":"paragraph"}]}
                """));

    [Fact]
    public void Attributes_a_heading_or_quote_may_not_carry_are_never_written()
    {
        var html = Body("""
            {"type":"heading","attrs":{"level":2,"id":"x","class":"evil","style":"color:red"},"content":[{"type":"text","text":"t"}]},
            {"type":"blockquote","attrs":{"cite":"https://example.com"},"content":[{"type":"paragraph"}]}
            """);

        Assert.Equal("<h2><span>t</span></h2><blockquote><p></p></blockquote>", html);
    }

    [Theory]
    [InlineData("underline", "<u>x</u>")]
    [InlineData("strike", "<del>x</del>")]
    [InlineData("code", "<code>x</code>")]
    public void Underline_strikethrough_and_code_are_their_semantic_elements(string mark, string html) =>
        Assert.Equal(
            $"<p>{html}</p>",
            Body($$"""{"type":"paragraph","content":[{"type":"text","text":"x","marks":[{"type":"{{mark}}"}]}]}"""));

    [Fact]
    public void Marks_nest_in_their_stored_order_first_innermost_with_a_link_outermost() =>
        Assert.Equal(
            "<p><a class=\"qb-link\" href=\"https://example.com/\"><code><del><u><em><strong>all</strong></em></u></del></code></a></p>",
            Body("""
                {"type":"paragraph","content":[{"type":"text","text":"all","marks":[
                  {"type":"bold"},{"type":"italic"},{"type":"underline"},{"type":"strike"},{"type":"code"},
                  {"type":"link","attrs":{"to":{"type":"url","href":"https://example.com/"}}}]}]}
                """));

    [Fact]
    public void Code_text_is_encoded() =>
        Assert.Equal(
            "<p><code>&lt;script&gt;alert(1)&lt;/script&gt;</code></p>",
            Body("""{"type":"paragraph","content":[{"type":"text","text":"<script>alert(1)</script>","marks":[{"type":"code"}]}]}"""));
}
