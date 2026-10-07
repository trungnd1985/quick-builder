using System.Text.Json;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Contracts.Schema;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// The collection schema contract document, its schema file and the code never disagree
/// (014 research CS-04, FR-017).
/// </summary>
public sealed class CollectionSchemaContractTests
{
    private static readonly string Contracts = Path.Combine(DraftApiTests.FindRepositoryRoot(), "docs", "contracts");

    private static string SchemaPath => Path.Combine(Contracts, "collection-schema", "v1.schema.json");

    private static JsonElement Schema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        return schema.RootElement.Clone();
    }

    private static JsonElement Defs => Schema().GetProperty("$defs");

    [Fact]
    public void The_embedded_schema_is_the_file_in_docs()
    {
        using var embedded = CollectionSchema.OpenSchema(1);
        using var copy = new MemoryStream();
        embedded.CopyTo(copy);

        Assert.Equal(File.ReadAllBytes(SchemaPath), copy.ToArray());
    }

    [Fact]
    public void The_schema_uses_only_the_page_schemas_subset()
    {
        Assert.Empty(SchemaSubsetValidator.UnsupportedKeywords(Schema()));
    }

    [Fact]
    public void Every_field_type_has_exactly_one_branch()
    {
        using var stream = CollectionSchema.OpenSchema(1);
        var types = SchemaSubsetValidator.Load(stream).BranchTypes("field");

        Assert.Equal(
            new[] { "boolean", "date", "file", "image", "multiReference", "number", "option", "reference", "richText", "text" },
            types.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("textValue")]
    [InlineData("numberValue")]
    [InlineData("booleanValue")]
    [InlineData("dateValue")]
    [InlineData("dateTimeValue")]
    [InlineData("mediaValue")]
    [InlineData("optionValue")]
    [InlineData("referenceValue")]
    [InlineData("multiReferenceValue")]
    [InlineData("itemValues")]
    public void Every_value_definition_the_document_names_exists(string definition)
    {
        Assert.True(Defs.TryGetProperty(definition, out _), definition);
        Assert.Contains(definition, File.ReadAllText(Path.Combine(Contracts, "collection-schema.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_limits_in_the_schema_match_the_code()
    {
        Assert.Equal(CollectionSchemaLimits.MaxFields, Schema().GetProperty("properties").GetProperty("fields").GetProperty("maxItems").GetInt32());
        Assert.Equal(CollectionSchemaLimits.MaxOptions, Defs.GetProperty("optionField").GetProperty("properties").GetProperty("options").GetProperty("maxItems").GetInt32());
        Assert.Equal(CollectionSchemaLimits.MaxReferences, Defs.GetProperty("multiReferenceValue").GetProperty("maxItems").GetInt32());
        Assert.Equal(CollectionSchemaLimits.MaxNameLength, Defs.GetProperty("name").GetProperty("maxLength").GetInt32());
        Assert.Equal(CollectionSchemaLimits.MaxHelpLength, Defs.GetProperty("help").GetProperty("maxLength").GetInt32());
        Assert.Equal(CollectionSchemaLimits.KeyPattern, Defs.GetProperty("key").GetProperty("pattern").GetString());
        Assert.Equal(CollectionSchemaLimits.MaxTextLength, Defs.GetProperty("textField").GetProperty("properties").GetProperty("maxLength").GetProperty("maximum").GetInt32());
        Assert.Equal(CollectionSchemaLimits.MaxDecimals, Defs.GetProperty("numberField").GetProperty("properties").GetProperty("decimals").GetProperty("maximum").GetInt32());
        Assert.Equal(CollectionSchemaLimits.MaxSafeInteger, Defs.GetProperty("numberValue").GetProperty("maximum").GetInt64());
    }

    [Fact]
    public void Every_invalid_example_is_in_the_manifest_and_every_entry_exists()
    {
        var directory = Path.Combine(Contracts, "collection-schema", "examples", "invalid");
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "expected.json")));
        var listed = expected.RootElement.EnumerateObject().Select(e => e.Name).Where(n => !n.StartsWith('$')).ToHashSet(StringComparer.Ordinal);
        var files = Directory.GetFiles(directory, "*.json").Select(Path.GetFileName).Where(n => n != "expected.json").ToHashSet(StringComparer.Ordinal);

        Assert.Equal(files.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }
}
