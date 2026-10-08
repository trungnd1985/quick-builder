using System.Text.Json;
using QuickBuilder.Pages.Contracts;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// The rich text rules the schema cannot say (page-schema.md §3 rules 10–12, 035 research
/// RT-02), and the questions the write paths ask of a rich text (RT-10, RT-13).
/// </summary>
public sealed class RichTextRulesTests
{
    private const string At = "/root/children/0/children/0/props/content";

    private static JsonElement Doc(params string[] blocks) =>
        JsonDocument.Parse($$"""{"type":"doc","content":[{{string.Join(",", blocks)}}]}""").RootElement;

    private static string Paragraph(string text = "x") => $$"""{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}""";

    private static string Image(string? alt = null, bool? decorative = null)
    {
        var attrs = """{"media":{"mediaId":"0192f5a1-0000-7000-8000-000000000010"}""";
        if (alt is not null)
        {
            attrs += $$""","alt":{{JsonSerializer.Serialize(alt)}}""";
        }

        if (decorative is { } d)
        {
            attrs += d ? ""","decorative":true""" : ""","decorative":false""";
        }

        return """{"type":"image","attrs":""" + attrs + "}}";
    }

    private static string Cell(string type = "tableCell") => $$"""{"type":"{{type}}","content":[{"type":"paragraph"}]}""";

    private static string Row(params string[] cells) => $$"""{"type":"tableRow","content":[{{string.Join(",", cells)}}]}""";

    private static string Table(params string[] rows) => $$"""{"type":"table","content":[{{string.Join(",", rows)}}]}""";

    private static string Grid(int rows = 2, int columns = 2, bool header = true) =>
        Table([.. Enumerable.Range(0, rows).Select(r => Row([.. Enumerable.Repeat(Cell(header && r == 0 ? "tableHeader" : "tableCell"), columns)]))]);

    [Theory]
    [InlineData(50, null)]
    [InlineData(51, "rich_text_too_many_images")]
    public void Rule_10_allows_50_images(int images, string? rule)
    {
        var failure = RichTextRules.Check(Doc([.. Enumerable.Repeat(Image("a"), images)]), At);

        Assert.Equal(rule, failure?.Rule);
        Assert.Equal(rule is null ? null : At, failure?.Location);
    }

    [Theory]
    [InlineData(20, null)]
    [InlineData(21, "rich_text_too_many_tables")]
    public void Rule_10_allows_20_tables(int tables, string? rule)
    {
        var failure = RichTextRules.Check(Doc([.. Enumerable.Repeat(Grid(), tables)]), At);

        Assert.Equal(rule, failure?.Rule);
    }

    [Fact]
    public void Rule_10_counts_images_before_tables()
    {
        var failure = RichTextRules.Check(Doc([.. Enumerable.Repeat(Grid(), 21), .. Enumerable.Repeat(Image("a"), 51)]), At);

        Assert.Equal("rich_text_too_many_images", failure?.Rule);
    }

    [Fact]
    public void Rule_11_refuses_a_ragged_table_at_the_first_row_that_differs()
    {
        var failure = RichTextRules.Check(Doc(Paragraph(), Table(Row(Cell(), Cell()), Row(Cell(), Cell()), Row(Cell()))), At);

        Assert.Equal(new RichTextRules.Failure("table_not_rectangular", $"{At}/content/1/content/2"), failure);
    }

    [Fact]
    public void Rule_12_refuses_a_header_cell_after_the_first_row()
    {
        var failure = RichTextRules.Check(Doc(Table(Row(Cell(), Cell()), Row(Cell(), Cell("tableHeader")))), At);

        Assert.Equal(new RichTextRules.Failure("table_header_misplaced", $"{At}/content/0/content/1/content/1"), failure);
    }

    [Fact]
    public void Rule_12_refuses_a_first_row_that_mixes_header_and_ordinary_cells()
    {
        var failure = RichTextRules.Check(Doc(Table(Row(Cell("tableHeader"), Cell()), Row(Cell(), Cell()))), At);

        Assert.Equal(new RichTextRules.Failure("table_header_misplaced", $"{At}/content/0/content/0/content/1"), failure);
    }

    [Fact]
    public void Rule_11_is_checked_before_rule_12()
    {
        var failure = RichTextRules.Check(Doc(Table(Row(Cell(), Cell("tableHeader")), Row(Cell()))), At);

        Assert.Equal("table_not_rectangular", failure?.Rule);
    }

    [Fact]
    public void A_table_at_the_limits_with_a_header_row_passes()
    {
        Assert.Null(RichTextRules.Check(Doc(Grid(200, 20)), At));
        Assert.Null(RichTextRules.Check(Doc(Grid(3, 3, header: false)), At));
    }

    [Fact]
    public void Images_without_alternative_text_are_listed_by_pointer()
    {
        var doc = Doc(Image("A shop"), Image(), Image("   "), Image(decorative: true), Image(alt: "", decorative: false), Paragraph());

        Assert.Equal([$"{At}/content/1", $"{At}/content/2", $"{At}/content/4"], RichTextRules.ImagesWithoutAlt(doc, At));
    }

    [Theory]
    [InlineData("""{"type":"heading","attrs":{"level":2}}""")]
    [InlineData("""{"type":"blockquote","content":[{"type":"paragraph"}]}""")]
    [InlineData("""{"type":"image","attrs":{"media":{"mediaId":"0192f5a1-0000-7000-8000-000000000010"}}}""")]
    [InlineData("""{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]}""")]
    [InlineData("""{"type":"paragraph","attrs":{"align":"center"}}""")]
    [InlineData("""{"type":"paragraph","content":[{"type":"text","text":"u","marks":[{"type":"underline"}]}]}""")]
    [InlineData("""{"type":"paragraph","content":[{"type":"text","text":"s","marks":[{"type":"strike"}]}]}""")]
    [InlineData("""{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"c","marks":[{"type":"code"}]}]}]}]}""")]
    public void Every_035_format_counts_as_extended(string block) =>
        Assert.True(RichTextRules.UsesExtendedFormats(Doc(block)));

    [Fact]
    public void Rich_text_with_only_015_formats_is_not_extended()
    {
        var doc = Doc(
            """{"type":"paragraph","content":[{"type":"text","text":"b","marks":[{"type":"bold"},{"type":"italic"},{"type":"link","attrs":{"to":{"type":"url","href":"https://example.com"}}}]},{"type":"hardBreak"}]}""",
            """{"type":"orderedList","attrs":{"start":3},"content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}""");

        Assert.False(RichTextRules.UsesExtendedFormats(doc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"type":"paragraph"}""")]
    [InlineData("""{"type":"paragraph"},{"type":"heading","attrs":{"level":2}}""")]
    [InlineData("""{"type":"blockquote","content":[{"type":"paragraph"}]}""")]
    [InlineData("""{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]}""")]
    [InlineData("""{"type":"paragraph","content":[{"type":"hardBreak"}]}""")]
    public void A_rich_text_without_text_or_images_is_empty(string blocks) =>
        Assert.True(RichTextRules.IsEmpty(JsonDocument.Parse($"{{\"type\":\"doc\",\"content\":[{blocks}]}}").RootElement));

    [Fact]
    public void One_image_or_one_character_is_not_empty()
    {
        Assert.False(RichTextRules.IsEmpty(Doc(Image())));
        Assert.False(RichTextRules.IsEmpty(Doc(Paragraph("x"))));
    }

    [Fact]
    public void Anything_that_is_not_a_document_is_not_extended_and_breaks_no_rule()
    {
        var notADocument = JsonDocument.Parse("\"text\"").RootElement;

        Assert.False(RichTextRules.UsesExtendedFormats(notADocument));
        Assert.Null(RichTextRules.Check(notADocument, At));
        Assert.Empty(RichTextRules.ImagesWithoutAlt(notADocument, At));
    }
}
