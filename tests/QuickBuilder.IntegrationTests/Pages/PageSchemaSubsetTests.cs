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
    public void Every_pattern_compiles_with_ecmascript_semantics()
    {
        // 011 research ED-06: the editor runs these patterns in JavaScript. RegexOptions
        // .ECMAScript refuses constructs JavaScript would read differently.
        foreach (var file in Directory.GetFiles(SchemaDirectory, "*.schema.json"))
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(file));

            Assert.All(Patterns(schema.RootElement), pattern =>
                _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.ECMAScript));
        }
    }

    [Fact]
    public void The_documented_subset_is_the_validators()
    {
        // The §7 bullet lists name every keyword the validator interprets or ignores.
        var document = File.ReadAllText(Path.Combine(SchemaDirectory, "..", "page-schema.md"));
        var section = document[document.IndexOf("## 7. The schema file", StringComparison.Ordinal)..document.IndexOf("## 8.", StringComparison.Ordinal)];
        var documented = System.Text.RegularExpressions.Regex.Matches(section, "`(\\$?[a-zA-Z]+)(?:: uuid)?`")
            .Select(m => m.Groups[1].Value)
            .Where(SchemaSubsetValidator.SupportedKeywords.Contains)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            documented.SetEquals(SchemaSubsetValidator.SupportedKeywords),
            $"page-schema.md §7 names [{string.Join(", ", documented.Order())}]; the validator supports [{string.Join(", ", SchemaSubsetValidator.SupportedKeywords.Order())}].");
    }

    private static IEnumerable<string> Patterns(JsonElement schema)
    {
        switch (schema.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in schema.EnumerateObject())
                {
                    if (property.Name == "pattern" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        yield return property.Value.GetString()!;
                    }
                    else
                    {
                        foreach (var nested in Patterns(property.Value))
                        {
                            yield return nested;
                        }
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in schema.EnumerateArray())
                {
                    foreach (var nested in Patterns(item))
                    {
                        yield return nested;
                    }
                }

                break;
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
