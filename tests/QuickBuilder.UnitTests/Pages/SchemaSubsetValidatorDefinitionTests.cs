using System.Text;
using System.Text.Json;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>
/// Checking a value against one named definition of a loaded schema (014 research CS-04,
/// contracts/module-surface.md §2).
/// </summary>
/// <remarks>
/// The collection schema uses it for item values, one definition per field type, and for
/// rich text values against the page schema's <c>rtDoc</c>, so there is one rich text
/// definition in the product.
/// </remarks>
public sealed class SchemaSubsetValidatorDefinitionTests
{
    private const string Schema = """
        {
          "type": "object",
          "$defs": {
            "word": { "type": "string", "pattern": "^[a-z]+$" },
            "pair": {
              "type": "object",
              "required": ["a"],
              "additionalProperties": false,
              "properties": { "a": { "$ref": "#/$defs/word" }, "b": { "type": "integer" } }
            }
          }
        }
        """;

    private static SchemaSubsetValidator Load(string schema) =>
        SchemaSubsetValidator.Load(new MemoryStream(Encoding.UTF8.GetBytes(schema)));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public void A_value_is_checked_against_the_named_definition_only()
    {
        var validator = Load(Schema);

        // Valid for the definition although the root would refuse it (the root wants an object).
        Assert.Null(validator.ValidateDefinition("word", Json("\"abc\"")));
        Assert.Equal("pattern", validator.ValidateDefinition("word", Json("\"ABC\""))!.Keyword);
    }

    [Fact]
    public void References_inside_the_definition_are_resolved()
    {
        var failure = Load(Schema).ValidateDefinition("pair", Json("""{"a":"X"}"""));

        Assert.Equal(("/a", "pattern"), (failure!.Location, failure.Keyword));
    }

    [Fact]
    public void Locations_are_relative_to_the_value()
    {
        var failure = Load(Schema).ValidateDefinition("pair", Json("""{"a":"x","b":1.5}"""));

        Assert.Equal("/b", failure!.Location);
    }

    [Fact]
    public void An_unknown_definition_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => Load(Schema).ValidateDefinition("nope", Json("1")));
    }

    [Fact]
    public void The_page_schemas_rich_text_definition_accepts_a_document_and_refuses_an_unknown_node()
    {
        using var stream = PageSchema.OpenSchema(1);
        var pageSchema = SchemaSubsetValidator.Load(stream);

        Assert.Null(pageSchema.ValidateDefinition(
            "rtDoc",
            Json("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello"}]}]}""")));

        var failure = pageSchema.ValidateDefinition("rtDoc", Json("""{"type":"doc","content":[{"type":"table"}]}"""));
        Assert.Equal("/content/0", failure!.Location);
    }
}
