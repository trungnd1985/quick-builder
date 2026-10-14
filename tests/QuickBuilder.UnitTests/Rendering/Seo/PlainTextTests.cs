using System.Text.Json;
using QuickBuilder.Rendering.Seo;

namespace QuickBuilder.UnitTests.Rendering.Seo;

/// <summary>
/// Metadata values as one line of text: rich text as its words, whitespace collapsed, and a
/// cut at a word with an ellipsis (017 FR-008, research SE-04).
/// </summary>
public sealed class PlainTextTests
{
    [Theory]
    [InlineData("  a \t b\n\nc  ", "a b c")]
    [InlineData("a  b", "a b")]
    [InlineData("   ", "")]
    public void Whitespace_collapses_to_single_spaces(string text, string expected)
    {
        Assert.Equal(expected, PlainText.Collapse(text));
    }

    [Fact]
    public void Rich_text_is_its_block_texts_joined_with_spaces_and_marks_dropped()
    {
        using var document = JsonDocument.Parse(
            """
            { "type": "doc", "content": [
              { "type": "heading", "attrs": { "level": 2 }, "content": [ { "type": "text", "text": "Title" } ] },
              { "type": "paragraph", "content": [ { "type": "text", "text": "Some " }, { "type": "text", "text": "bold", "marks": [ { "type": "bold" } ] }, { "type": "text", "text": " words" } ] },
              { "type": "paragraph", "content": [ { "type": "text", "text": "one" }, { "type": "hardBreak" }, { "type": "text", "text": "two" } ] },
              { "type": "paragraph" },
              { "type": "image", "attrs": { "mediaId": "0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e30" } }
            ] }
            """);

        Assert.Equal("Title Some bold words one two", PlainText.Of(document.RootElement));
    }

    [Fact]
    public void A_value_within_the_limit_is_kept()
    {
        Assert.Equal("Short title", PlainText.Clean("Short   title", 200));
        Assert.Equal(new string('a', 200), PlainText.Clean(new string('a', 200), 200));
    }

    [Fact]
    public void A_longer_value_is_cut_at_the_last_word_that_fits_with_an_ellipsis()
    {
        var cut = PlainText.Clean("one two three four", 12);

        Assert.Equal("one two…", cut);
        Assert.True(cut.Length <= 12);
    }

    [Fact]
    public void A_single_word_longer_than_the_limit_is_cut_inside_the_word()
    {
        Assert.Equal(new string('a', 9) + "…", PlainText.Clean(new string('a', 25), 10));
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var cut = PlainText.Clean(new string('a', 8) + "😀😀😀", 10);

        Assert.Equal(new string('a', 8) + "…", cut);
    }

    [Theory]
    [InlineData("title", 200)]
    [InlineData("ogTitle", 200)]
    [InlineData("description", 500)]
    [InlineData("ogDescription", 500)]
    public void The_limits_are_the_page_schema_s(string field, int max)
    {
        Assert.Equal(max, PlainText.MaxLength(field));
    }
}
