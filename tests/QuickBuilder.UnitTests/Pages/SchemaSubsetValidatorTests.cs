using System.Text;
using System.Text.Json;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// Each keyword of the supported subset behaves as JSON Schema 2020-12 says (research P-09).
/// </summary>
/// <remarks>
/// One passing and one failing value per keyword, against a schema that uses only that
/// keyword. The reported location is the value that failed.
/// </remarks>
public sealed class SchemaSubsetValidatorTests
{
    [Theory]
    [InlineData("""{"type":"string"}""", "\"x\"", "1")]
    [InlineData("""{"type":"integer"}""", "3", "3.5")]
    [InlineData("""{"type":"integer"}""", "3.0", "\"3\"")]
    [InlineData("""{"type":"number"}""", "3.5", "true")]
    [InlineData("""{"type":["string","null"]}""", "null", "1")]
    [InlineData("""{"type":"boolean"}""", "false", "0")]
    [InlineData("""{"type":"array"}""", "[]", "{}")]
    [InlineData("""{"type":"object"}""", "{}", "[]")]
    [InlineData("""{"const":1}""", "1.0", "2")]
    [InlineData("""{"const":{"a":[1]}}""", """{"a":[1]}""", """{"a":[2]}""")]
    [InlineData("""{"enum":["a","b"]}""", "\"b\"", "\"c\"")]
    [InlineData("""{"pattern":"^[a-z]+$"}""", "\"abc\"", "\"aBc\"")]
    [InlineData("""{"minLength":2}""", "\"ab\"", "\"a\"")]
    [InlineData("""{"maxLength":2}""", "\"😀😀\"", "\"abc\"")]
    [InlineData("""{"minimum":2}""", "2", "1.9")]
    [InlineData("""{"maximum":2}""", "2", "2.1")]
    [InlineData("""{"maxItems":1}""", "[1]", "[1,2]")]
    [InlineData("""{"format":"uuid"}""", "\"0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f\"", "\"{0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e0f}\"")]
    [InlineData("""{"required":["a"]}""", """{"a":1}""", """{"b":1}""")]
    [InlineData("""{"additionalProperties":false,"properties":{"a":{}}}""", """{"a":1}""", """{"a":1,"b":2}""")]
    [InlineData("""{"additionalProperties":{"type":"string"}}""", """{"a":"x"}""", """{"a":1}""")]
    [InlineData("""{"properties":{"a":{"type":"string"}}}""", """{"a":"x","b":1}""", """{"a":1}""")]
    [InlineData("""{"items":{"type":"string"}}""", """["a","b"]""", """["a",1]""")]
    [InlineData("""{"$defs":{"s":{"type":"string"}},"$ref":"#/$defs/s"}""", "\"x\"", "1")]
    public void Each_keyword_accepts_and_refuses(string schema, string good, string bad)
    {
        var validator = Load(schema);

        Assert.Null(validator.Validate(Parse(good)));
        Assert.NotNull(validator.Validate(Parse(bad)));
    }

    [Fact]
    public void A_format_other_than_uuid_is_ignored_by_no_one_it_is_refused_at_load()
    {
        Assert.Throws<InvalidOperationException>(() => Load("""{"format":"email"}"""));
    }

    [Theory]
    [InlineData("""{"patternProperties":{"^a":{}}}""", "patternProperties")]
    [InlineData("""{"properties":{"a":{"anyOf":[{}]}}}""", "anyOf")]
    [InlineData("""{"$ref":"https://example.com/schema"}""", "$ref: https://example.com/schema")]
    [InlineData("""{"$defs":{"x":{"if":{}}}}""", "if")]
    public void A_schema_outside_the_subset_is_refused_when_loaded(string schema, string keyword)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Load(schema));

        Assert.Contains(keyword, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void One_of_picks_the_branch_named_by_the_type_property()
    {
        var validator = Load("""
            {
              "$defs": {
                "a": { "properties": { "type": { "const": "a" }, "n": { "type": "integer" } } },
                "b": { "properties": { "type": { "const": "b" }, "n": { "type": "string" } } }
              },
              "oneOf": [ { "$ref": "#/$defs/a" }, { "$ref": "#/$defs/b" } ]
            }
            """);

        Assert.Null(validator.Validate(Parse("""{"type":"a","n":1}""")));
        Assert.Null(validator.Validate(Parse("""{"type":"b","n":"x"}""")));
        Assert.Equal(("/n", "type"), Describe(validator.Validate(Parse("""{"type":"b","n":1}"""))));
        Assert.Equal(("", "discriminator"), Describe(validator.Validate(Parse("""{"type":"c"}"""))));
    }

    [Fact]
    public void The_location_is_the_deepest_failing_value()
    {
        var validator = Load("""{"properties":{"a":{"items":{"properties":{"b":{"type":"string"}}}}}}""");

        var failure = validator.Validate(Parse("""{"a":[{"b":"x"},{"b":2}]}"""));

        Assert.Equal(("/a/1/b", "type"), Describe(failure));
    }

    [Fact]
    public void Pointer_tokens_are_escaped()
    {
        var validator = Load("""{"additionalProperties":false}""");

        Assert.Equal("/a~1b~0c", validator.Validate(Parse("""{"a/b~c":1}"""))!.Location);
    }

    [Fact]
    public void The_shipped_page_schema_uses_only_the_subset()
    {
        using var schema = File.OpenRead(Path.Combine(ContractFiles.PageSchemaDirectory, "v1.schema.json"));

        var validator = SchemaSubsetValidator.Load(schema);

        Assert.NotNull(validator);
    }

    private static SchemaSubsetValidator Load(string schema) =>
        SchemaSubsetValidator.Load(new MemoryStream(Encoding.UTF8.GetBytes(schema)));

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static (string, string) Describe(SchemaFailure? failure) =>
        failure is null ? ("(none)", "(none)") : (failure.Location, failure.Keyword);
}
