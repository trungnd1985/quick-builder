using System.Text.Json;

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

    private static string Image(Guid media, string attrs = "") =>
        """{"type":"image","attrs":{"media":{"mediaId":"ID"}ATTRS}}"""
            .Replace("ID", media.ToString(), StringComparison.Ordinal)
            .Replace("ATTRS", attrs, StringComparison.Ordinal);

    [Fact]
    public void An_image_is_a_figure_with_its_alternative_text_served_from_the_media_address()
    {
        _harness.BasePath = "/site";

        Assert.Equal(
            $"<figure class=\"qb-rt-image\"><img src=\"/site/_media/{RenderHarness.Photo:N}.jpg\" alt=\"The &quot;shop&quot;\" loading=\"lazy\" decoding=\"async\"></figure>",
            Body(Image(RenderHarness.Photo, ",\"alt\":\"The \\\"shop\\\"\"")));
    }

    [Fact]
    public void A_decorative_image_has_empty_alternative_text_whatever_it_stored() =>
        Assert.Contains(
            "alt=\"\"",
            Body(Image(RenderHarness.Photo, ",\"alt\":\"Ignored\",\"decorative\":true")),
            StringComparison.Ordinal);

    [Fact]
    public void Hostile_alternative_text_is_encoded() =>
        Assert.Contains(
            "alt=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\"",
            Body(Image(RenderHarness.Photo, ",\"alt\":\"\\\"><script>alert(1)</script>\"")),
            StringComparison.Ordinal);

    [Fact]
    public void An_image_whose_file_is_gone_or_belongs_to_another_site_renders_nothing() =>
        Assert.Equal(
            "<p><span>before</span></p><p><span>after</span></p>",
            Body($$"""
                {"type":"paragraph","content":[{"type":"text","text":"before"}]},
                {{Image(Guid.Parse("66666666-6666-4666-8666-666666666666"), ",\"alt\":\"gone\"")}},
                {"type":"paragraph","content":[{"type":"text","text":"after"}]}
                """));

    [Fact]
    public void A_measured_image_offers_its_variants_sized_to_the_text_and_never_wider_than_itself()
    {
        var (html, _) = _harness.Block(
            """
            {"id":"s","type":"section","props":{"width":"contained"},"children":[
              {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[IMAGES]}},"children":[]}]}
            """.Replace("IMAGES", $"{Image(RenderHarness.LargePhoto, ",\"alt\":\"Harbour\"")},{Image(RenderHarness.SmallLogo, ",\"alt\":\"Logo\"")}", StringComparison.Ordinal));

        var id = RenderHarness.LargePhoto.ToString("N");
        var srcset = string.Join(", ", new[] { 320, 640, 960, 1280, 1920, 2560 }.Select(w => $"/_media/{id}-v1-w{w}.jpg {w}w"));
        Assert.Contains(
            $"<figure class=\"qb-rt-image\"><img src=\"/_media/{id}-v1-w1280.jpg\" srcset=\"{srcset}\" sizes=\"(max-width: 1200px) 100vw, 1200px\" width=\"6000\" height=\"4000\" alt=\"Harbour\" loading=\"lazy\" decoding=\"async\"></figure>",
            html,
            StringComparison.Ordinal);
        Assert.Contains("sizes=\"(max-width: 800px) 100vw, 800px\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Rich_text_images_are_render_inputs_and_page_dependencies()
    {
        using var document = JsonDocument.Parse(
            """
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{},"children":[
                {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[IMAGE]}},"children":[]}]}]}}
            """.Replace("IMAGE", Image(RenderHarness.Photo), StringComparison.Ordinal));

        var references = new QuickBuilder.Rendering.Domain.RenderReferences();
        references.Collect(document.RootElement);

        Assert.Equal([RenderHarness.Photo], references.Media);
        Assert.Equal([RenderHarness.Photo], QuickBuilder.Pages.Domain.DependencyExtractor.Media(document.RootElement.GetRawText()));
    }

    [Fact]
    public void A_table_scrolls_in_its_own_focusable_area_with_its_header_cells_scoped_to_their_columns() =>
        Assert.Equal(
            "<div class=\"qb-rt-table\" tabindex=\"0\"><table><tbody>"
                + "<tr><th scope=\"col\"><p><span>Day</span></p></th><th scope=\"col\"><p><span>Hours</span></p></th></tr>"
                + "<tr><td><p><span>Sunday</span></p></td><td><p><strong>Closed</strong></p><p></p></td></tr>"
                + "</tbody></table></div>",
            Body("""
                {"type":"table","content":[
                  {"type":"tableRow","content":[
                    {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Day"}]}]},
                    {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"Hours"}]}]}]},
                  {"type":"tableRow","content":[
                    {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Sunday"}]}]},
                    {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Closed","marks":[{"type":"bold"}]}]},{"type":"paragraph"}]}]}]}
                """));

    [Fact]
    public void A_table_without_a_header_row_has_only_data_cells_and_never_spans() =>
        Assert.Equal(
            "<div class=\"qb-rt-table\" tabindex=\"0\"><table><tbody><tr><td><p></p></td></tr></tbody></table></div>",
            Body("""{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","attrs":{"colspan":3,"style":"x"},"content":[{"type":"paragraph"}]}]}]}"""));
}
