using System.Text.Json.Nodes;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Cms.Infrastructure.Schema;
using static QuickBuilder.UnitTests.Cms.SchemaTestKit;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// Item values against their fields: shape through the contract's value definitions, then
/// the field's settings (contract §3, research CS-03).
/// </summary>
public sealed class ItemValuesTests
{
    private static readonly ItemValueValidator Validator = new(new CollectionSchemaValidator());

    private static string StateOf(Field field, string? json) =>
        Validator.Check(field, json is null ? null : JsonNode.Parse(json)).ToString();

    [Theory]
    [InlineData("\"Hello\"", "Valid")]
    [InlineData("12", "Malformed")]
    [InlineData("\"two\\nlines\"", "NeedsAttention")]
    public void Text_values(string json, string expected) => Assert.Equal(expected, StateOf(Text("f1"), json));

    [Fact]
    public void Text_longer_than_its_field_allows_needs_attention_and_length_counts_code_points()
    {
        Assert.Equal("Valid", StateOf(Text("f1", maxLength: 2), "\"😀😀\""));
        Assert.Equal("NeedsAttention", StateOf(Text("f1", maxLength: 2), "\"abc\""));
        Assert.Equal("Valid", StateOf(Text("f1", multiline: true), "\"two\\nlines\""));
    }

    [Theory]
    [InlineData("7", 0, null, null, "Valid")]
    [InlineData("7.5", 0, null, null, "NeedsAttention")]
    [InlineData("7.25", 2, null, null, "Valid")]
    [InlineData("7.255", 2, null, null, "NeedsAttention")]
    [InlineData("3", 0, 5, null, "NeedsAttention")]
    [InlineData("30", 0, null, 10, "NeedsAttention")]
    [InlineData("9007199254740992", 0, null, null, "Malformed")]
    [InlineData("\"7\"", 0, null, null, "Malformed")]
    public void Number_values(string json, int decimals, int? min, int? max, string expected)
    {
        var field = Of(FieldType.Number, "f1") with { Decimals = decimals, Min = min, Max = max };

        Assert.Equal(expected, StateOf(field, json));
    }

    [Theory]
    [InlineData(false, "\"2026-10-07\"", "Valid")]
    [InlineData(false, "\"2026-02-30\"", "Malformed")]
    [InlineData(false, "\"2026-10-07T10:00:00Z\"", "Malformed")]
    [InlineData(true, "\"2026-10-07T10:00:00Z\"", "Valid")]
    [InlineData(true, "\"2026-10-07T10:00:00+07:00\"", "Malformed")]
    [InlineData(true, "\"2026-10-07\"", "Malformed")]
    public void Date_values_have_one_textual_form(bool withTime, string json, string expected)
    {
        Assert.Equal(expected, StateOf(Of(FieldType.Date, "f1") with { WithTime = withTime }, json));
    }

    [Fact]
    public void Rich_text_is_checked_against_the_page_schema()
    {
        var field = Of(FieldType.RichText, "f1");

        Assert.Equal("Valid", StateOf(field, """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]}]}"""));
        Assert.Equal("Malformed", StateOf(field, """{"type":"doc","content":[{"type":"table"}]}"""));
        Assert.Equal("Malformed", StateOf(field, "\"<p>raw html</p>\""));
    }

    [Fact]
    public void Media_option_and_reference_values()
    {
        Assert.Equal("Valid", StateOf(Of(FieldType.Image, "f1"), """{"mediaId":"0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f"}"""));
        Assert.Equal("Malformed", StateOf(Of(FieldType.Image, "f1"), "\"0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f\""));
        Assert.Equal("Valid", StateOf(Of(FieldType.Option, "f1"), "\"o1\""));
        Assert.Equal("NeedsAttention", StateOf(Of(FieldType.Option, "f1"), "\"o9\""));
        Assert.Equal("Valid", StateOf(Of(FieldType.Reference, "f1"), "\"0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f\""));
        Assert.Equal("Malformed", StateOf(Of(FieldType.MultiReference, "f1"), "[]"));
        Assert.Equal(
            "NeedsAttention",
            StateOf(Of(FieldType.MultiReference, "f1"), """["0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f","0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f"]"""));
    }

    [Fact]
    public void No_value_is_empty_or_missing_and_a_boolean_is_never_missing()
    {
        Assert.Equal("Empty", StateOf(Text("f1"), null));
        Assert.Equal("MissingRequired", StateOf(Text("f1", required: true), null));
        Assert.Equal("Empty", StateOf(Of(FieldType.Boolean, "f1") with { Required = true }, null));
    }

    [Fact]
    public void Values_are_kept_by_field_id_and_setting_null_removes_one()
    {
        var values = ItemValues.Empty();
        values.Set("f1", JsonValue.Create("Hello"));
        values.Set("f2", JsonValue.Create(3));
        values.Set("f2", null);

        Assert.Equal("""{"schemaVersion":1,"values":{"f1":"Hello"}}""", values.Write().ToJsonString());
        Assert.False(values.Has("f2"));
    }
}
