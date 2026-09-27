using System.Text.Json;
using QuickBuilder.Pages.Infrastructure.Schema;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// The shipped page schema uses only the keywords the Pages validator interprets
/// (010 research P-09).
/// </summary>
/// <remarks>
/// The validator refuses such a schema when the application starts; this makes the same
/// check fail the build first, naming the keyword and where it is, so a block type added in
/// #11 cannot quietly outgrow the validator.
/// </remarks>
public sealed class PageSchemaSubsetTests
{
    private static readonly string SchemaDirectory =
        Path.Combine(DraftApiTests.FindRepositoryRoot(), "docs", "contracts", "page-schema");

    [Fact]
    public void Every_shipped_schema_uses_only_the_subset()
    {
        foreach (var file in Directory.GetFiles(SchemaDirectory, "*.schema.json"))
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(file));

            var unsupported = SchemaSubsetValidator.UnsupportedKeywords(schema.RootElement).ToList();

            Assert.True(
                unsupported.Count == 0,
                $"{Path.GetFileName(file)} uses keywords the validator does not support: "
                + string.Join(", ", unsupported.Select(u => $"'{u.Keyword}' at {u.Location}")));
        }
    }

    [Fact]
    public void The_guard_fires_on_a_keyword_outside_the_subset()
    {
        using var schema = JsonDocument.Parse("""{"$defs":{"block":{"patternProperties":{"^x-":{}}}}}""");

        var unsupported = SchemaSubsetValidator.UnsupportedKeywords(schema.RootElement).ToList();

        Assert.Equal([("patternProperties", "/$defs/block/patternProperties")], unsupported);
    }
}
