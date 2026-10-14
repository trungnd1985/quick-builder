using System.Text.RegularExpressions;
using QuickBuilder.Rendering.Seo.StructuredData;

namespace QuickBuilder.UnitTests.Rendering.Seo;

/// <summary>
/// types.json, as the renderer loads it, says what the tables of structured-data.md §3 say
/// (017 research SE-19). The admin's structuredDataTypes.spec.ts holds the editor to the same.
/// </summary>
public sealed partial class StructuredDataTypesContractTests
{
    [Fact]
    public void The_types_match_the_contract_tables()
    {
        var documented = Documented();

        Assert.Equal(documented.Keys.Order(StringComparer.Ordinal), StructuredDataTypes.All.Keys.Order(StringComparer.Ordinal));
        foreach (var (typeName, properties) in documented)
        {
            var type = StructuredDataTypes.All[typeName];
            Assert.Equal(properties.Select(p => p.Name), type.Properties.Select(p => p.Name));
            foreach (var property in properties)
            {
                var loaded = type.Property(property.Name)!;
                Assert.True(property.Required == loaded.Required, $"{typeName}.{property.Name}: required");
                Assert.True(property.Default == loaded.Default, $"{typeName}.{property.Name}: default {property.Default} vs {loaded.Default}");
                Assert.Equal(property.Accepts.Order(StringComparer.Ordinal), loaded.Accepts.Order(StringComparer.Ordinal));
            }
        }
    }

    private sealed record Row(string Name, bool Required, IReadOnlyList<string> Accepts, string? Default);

    /// <summary>The tables of §3, read as the document writes them.</summary>
    private static Dictionary<string, List<Row>> Documented()
    {
        var types = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
        List<Row>? current = null;
        foreach (var line in File.ReadAllLines(RepositoryFiles.Path("docs", "contracts", "structured-data.md")))
        {
            if (TypeHeading().Match(line) is { Success: true } heading)
            {
                current = types[heading.Groups["name"].Value] = [];
                continue;
            }

            if (line.StartsWith("## 4", StringComparison.Ordinal))
            {
                break;
            }

            if (current is null || PropertyRow().Match(line) is not { Success: true } row)
            {
                continue;
            }

            var accepts = row.Groups["accepts"].Value.Trim();
            current.Add(new Row(
                row.Groups["name"].Value,
                row.Groups["required"].Success,
                accepts.StartsWith("as `", StringComparison.Ordinal) ? types.Values.SelectMany(t => t).First(r => r.Name == accepts[4..^1]).Accepts : Accepts(accepts),
                Default(row.Groups["default"].Value.Trim())));
        }

        return types;
    }

    private static List<string> Accepts(string cell) =>
        [.. cell.Split(',').Select(part => part.Trim().Trim('`') switch
        {
            "rich text" => "richText",
            "text template" => "template",
            var other when other.StartsWith("reference", StringComparison.Ordinal) => "reference",
            var other => other,
        })];

    private static string? Default(string cell) => cell switch
    {
        "—" => null,
        "the item's name" => "name",
        _ => cell.Trim('`'),
    };

    [GeneratedRegex("^### `(?<name>[A-Za-z]+)`$")]
    private static partial Regex TypeHeading();

    [GeneratedRegex("^\\| `(?<name>[A-Za-z]+)`(?<required> \\(required\\))? \\| (?<accepts>[^|]+) \\| (?<default>[^|]+) \\|")]
    private static partial Regex PropertyRow();
}
