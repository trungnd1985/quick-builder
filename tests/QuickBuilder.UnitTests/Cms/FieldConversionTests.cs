using System.Text.Json.Nodes;
using QuickBuilder.Cms.Domain;
using static QuickBuilder.UnitTests.Cms.SchemaTestKit;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// Every value conversion of contract §7, with its fixed formats, UTC dates and rounding
/// (014 research CS-07, SC-004).
/// </summary>
public sealed class FieldConversionTests
{
    private static readonly Guid Image = Guid.Parse("0192f5a0-0000-7000-8000-0000000000a1");
    private static readonly Guid Pdf = Guid.Parse("0192f5a0-0000-7000-8000-0000000000a2");
    private static readonly Guid Gone = Guid.Parse("0192f5a0-0000-7000-8000-0000000000a3");

    private static MediaKind Kind(Guid id) => id == Image ? MediaKind.Image : id == Pdf ? MediaKind.Document : MediaKind.Missing;

    private static (string Outcome, string? Value) Run(Field before, Field after, string json)
    {
        var converted = FieldConversion.Convert(before, after, JsonNode.Parse(json)!, Kind);
        return (converted.Outcome.ToString(), converted.Value?.ToJsonString());
    }

    private static Field Number(int decimals = 0, decimal? min = null, decimal? max = null) =>
        Of(FieldType.Number, "f1") with { Decimals = decimals, Min = min, Max = max };

    private static Field Date(bool withTime) => Of(FieldType.Date, "f1") with { WithTime = withTime };

    [Theory]
    [InlineData("\"7\"", 0, "Converted", "7")]
    [InlineData("\" -12 \"", 0, "Converted", "-12")]
    [InlineData("\"12.5\"", 0, "PartlyLost", "13")]
    [InlineData("\"-12.5\"", 0, "PartlyLost", "-13")]
    [InlineData("\"3.14159\"", 2, "PartlyLost", "3.14")]
    [InlineData("\"about 7\"", 0, "Cleared", null)]
    [InlineData("\"1,234\"", 0, "Cleared", null)]
    [InlineData("\"1e3\"", 0, "Cleared", null)]
    [InlineData("\"\"", 0, "Cleared", null)]
    [InlineData("\"9007199254740992\"", 0, "Cleared", null)]
    public void Text_to_number_uses_one_fixed_format_and_rounds_half_away_from_zero(string text, int decimals, string outcome, string? stored)
    {
        Assert.Equal((outcome, stored), Run(Text("f1"), Number(decimals), text));
    }

    [Fact]
    public void Text_to_number_clears_values_outside_the_new_range()
    {
        Assert.Equal(("Cleared", null), Run(Text("f1"), Number(min: 10), "\"5\""));
        Assert.Equal(("Cleared", null), Run(Text("f1"), Number(max: 10), "\"50\""));
        Assert.Equal(("Converted", "10"), Run(Text("f1"), Number(min: 10, max: 10), "\"10\""));
    }

    [Theory]
    [InlineData("7", "\"7\"")]
    [InlineData("7.50", "\"7.5\"")]
    [InlineData("-0.25", "\"-0.25\"")]
    [InlineData("1234567", "\"1234567\"")]
    public void Number_to_text_is_invariant_and_shortest(string number, string text)
    {
        Assert.Equal(("Converted", text), Run(Number(2), Text("f1"), number));
    }

    [Theory]
    [InlineData(false, "\"2026-10-07\"", "Converted", "\"2026-10-07\"")]
    [InlineData(false, "\" 2026-10-07 \"", "Converted", "\"2026-10-07\"")]
    [InlineData(false, "\"07/10/2026\"", "Cleared", null)]
    [InlineData(false, "\"2026-02-30\"", "Cleared", null)]
    [InlineData(false, "\"2026-10-07T10:00:00Z\"", "Cleared", null)]
    [InlineData(true, "\"2026-10-07\"", "Converted", "\"2026-10-07T00:00:00Z\"")]
    [InlineData(true, "\"2026-10-07T10:30:00Z\"", "Converted", "\"2026-10-07T10:30:00Z\"")]
    [InlineData(true, "\"2026-10-07T10:30:00+07:00\"", "Converted", "\"2026-10-07T03:30:00Z\"")]
    [InlineData(true, "\"2026-10-07T10:30Z\"", "Converted", "\"2026-10-07T10:30:00Z\"")]
    [InlineData(true, "\"2026-10-07T10:30:00.250Z\"", "PartlyLost", "\"2026-10-07T10:30:00Z\"")]
    [InlineData(true, "\"tomorrow\"", "Cleared", null)]
    public void Text_to_date_accepts_iso_forms_and_stores_utc(bool withTime, string text, string outcome, string? stored)
    {
        Assert.Equal((outcome, stored), Run(Text("f1"), Date(withTime), text));
    }

    [Fact]
    public void Dates_become_text_in_their_stored_form()
    {
        Assert.Equal(("Converted", "\"2026-10-07\""), Run(Date(false), Text("f1"), "\"2026-10-07\""));
        Assert.Equal(("Converted", "\"2026-10-07T10:30:00Z\""), Run(Date(true), Text("f1"), "\"2026-10-07T10:30:00Z\""));
    }

    [Fact]
    public void A_date_gains_midnight_utc_and_a_moment_keeps_its_utc_calendar_date()
    {
        Assert.Equal(("Converted", "\"2026-10-07T00:00:00Z\""), Run(Date(false), Date(true), "\"2026-10-07\""));
        Assert.Equal(("Converted", "\"2026-10-07\""), Run(Date(true), Date(false), "\"2026-10-07T00:00:00Z\""));
        Assert.Equal(("PartlyLost", "\"2026-10-07\""), Run(Date(true), Date(false), "\"2026-10-07T23:59:59Z\""));
    }

    [Fact]
    public void Options_become_their_label_and_labels_become_options_ignoring_case_and_spaces()
    {
        var category = Of(FieldType.Option, "f1") with { Options = [new("o1", "News"), new("o2", "Guides")] };

        Assert.Equal(("Converted", "\"Guides\""), Run(category, Text("f1"), "\"o2\""));
        Assert.Equal(("Converted", "\"o1\""), Run(Text("f1"), category, "\" NEWS \""));
        Assert.Equal(("Cleared", null), Run(Text("f1"), category, "\"Events\""));
    }

    [Fact]
    public void Booleans_become_true_or_false()
    {
        Assert.Equal(("Converted", "\"true\""), Run(Of(FieldType.Boolean, "f1"), Text("f1"), "true"));
        Assert.Equal(("Converted", "\"false\""), Run(Of(FieldType.Boolean, "f1"), Text("f1"), "false"));
    }

    [Fact]
    public void Images_are_files_and_only_images_stay_images()
    {
        var image = $$"""{"mediaId":"{{Image}}"}""";
        var pdf = $$"""{"mediaId":"{{Pdf}}"}""";
        var gone = $$"""{"mediaId":"{{Gone}}"}""";

        Assert.Equal("Kept", Run(Of(FieldType.Image, "f1"), Of(FieldType.File, "f1"), image).Outcome);
        Assert.Equal("Kept", Run(Of(FieldType.File, "f1"), Of(FieldType.Image, "f1"), image).Outcome);
        Assert.Equal(("Cleared", null), Run(Of(FieldType.File, "f1"), Of(FieldType.Image, "f1"), pdf));
        Assert.Equal("Missing", Run(Of(FieldType.File, "f1"), Of(FieldType.Image, "f1"), gone).Outcome);
    }

    [Fact]
    public void References_become_lists_of_one_and_lists_keep_their_first_item()
    {
        const string a = "0192f5a0-0000-7000-8000-000000000001";
        const string b = "0192f5a0-0000-7000-8000-000000000002";

        Assert.Equal(("Converted", $"[\"{a}\"]"), Run(Of(FieldType.Reference, "f1"), Of(FieldType.MultiReference, "f1"), $"\"{a}\""));
        Assert.Equal(("Converted", $"\"{a}\""), Run(Of(FieldType.MultiReference, "f1"), Of(FieldType.Reference, "f1"), $"[\"{a}\"]"));
        Assert.Equal(("PartlyLost", $"\"{a}\""), Run(Of(FieldType.MultiReference, "f1"), Of(FieldType.Reference, "f1"), $"[\"{a}\",\"{b}\"]"));
    }

    [Fact]
    public void Text_becomes_one_paragraph_per_line()
    {
        var (outcome, stored) = Run(Text("f1", multiline: true), Of(FieldType.RichText, "f1"), "\"One\\nTwo\\n\\nFour\"");

        Assert.Equal("Converted", outcome);
        Assert.Equal(
            """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"One"}]},{"type":"paragraph","content":[{"type":"text","text":"Two"}]},{"type":"paragraph"},{"type":"paragraph","content":[{"type":"text","text":"Four"}]}]}""",
            stored);
    }

    [Fact]
    public void Past_five_hundred_lines_lines_share_paragraphs_and_nothing_is_lost()
    {
        var text = string.Join('\n', Enumerable.Range(1, 1200).Select(i => $"Line {i}"));

        var document = FieldConversion.TextToRichText(text);

        var blocks = document["content"]!.AsArray();
        Assert.True(blocks.Count <= 500);
        var words = blocks.SelectMany(b => b!["content"]!.AsArray()).Where(n => n!["type"]!.GetValue<string>() == "text").Select(n => n!["text"]!.GetValue<string>());
        Assert.Equal(Enumerable.Range(1, 1200).Select(i => $"Line {i}"), words);
        Assert.All(blocks, b => Assert.True(b!["content"]!.AsArray().Count <= 1000));
    }

    [Fact]
    public void Rich_text_becomes_text_and_says_when_formatting_is_lost()
    {
        const string plain = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello"}]},{"type":"paragraph","content":[{"type":"text","text":"World"}]}]}""";
        const string bold = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi","marks":[{"type":"bold"}]}]}]}""";
        const string list = """{"type":"doc","content":[{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"A"}]}]}]}]}""";

        Assert.Equal(("Converted", "\"Hello\\nWorld\""), Run(Of(FieldType.RichText, "f1"), Text("f1", multiline: true, maxLength: 5000), plain));
        Assert.Equal(("Converted", "\"Hello World\""), Run(Of(FieldType.RichText, "f1"), Text("f1"), plain));
        Assert.Equal(("PartlyLost", "\"Hi\""), Run(Of(FieldType.RichText, "f1"), Text("f1"), bold));
        Assert.Equal(("PartlyLost", "\"A\""), Run(Of(FieldType.RichText, "f1"), Text("f1"), list));
        Assert.Equal(("PartlyLost", "\"Hel\""), Run(Of(FieldType.RichText, "f1"), Text("f1", maxLength: 3), plain));
        Assert.Equal(("Cleared", null), Run(Of(FieldType.RichText, "f1"), Text("f1"), """{"type":"doc","content":[{"type":"paragraph"}]}"""));
    }

    [Fact]
    public void Several_lines_become_one_joined_with_spaces()
    {
        Assert.Equal(("Converted", "\"a b c\""), Run(Text("f1", multiline: true), Text("f1"), "\"a\\nb\\r\\nc\""));
        Assert.Equal(("Kept", "\"abc\""), Run(Text("f1", multiline: true), Text("f1"), "\"abc\""));
    }

    [Fact]
    public void Fewer_decimal_places_round_and_say_so()
    {
        Assert.Equal(("PartlyLost", "2.5"), Run(Number(2), Number(1), "2.45"));
        Assert.Equal(("Kept", "2.5"), Run(Number(2), Number(1), "2.5"));
    }

    [Fact]
    public void A_removed_option_clears_the_values_that_chose_it()
    {
        var before = Of(FieldType.Option, "f1") with { Options = [new("o1", "A"), new("o2", "B")] };
        var after = Of(FieldType.Option, "f1") with { Options = [new("o1", "A")] };

        Assert.Equal(("Cleared", null), Run(before, after, "\"o2\""));
        Assert.Equal("Kept", Run(before, after, "\"o1\"").Outcome);
    }

    [Fact]
    public void Tightened_settings_keep_values_for_the_caller_to_measure()
    {
        Assert.Equal(("Kept", "\"abcdef\""), Run(Text("f1"), Text("f1", maxLength: 3), "\"abcdef\""));
        Assert.Equal(("Kept", "50"), Run(Number(), Number(max: 10), "50"));
    }
}
