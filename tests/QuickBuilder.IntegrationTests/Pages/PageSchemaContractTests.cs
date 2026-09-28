using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuickBuilder.Pages.Contracts;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// The page schema contract document and the schema the product validates against never
/// disagree (010 FR-022, SC-007, research P-09).
/// </summary>
/// <remarks>
/// The document is prose for people; the schema file is the definition machines use. These
/// checks read the tables the document commits to — node types, limits, reserved segments —
/// and compare them with the schema file and the constants the code enforces, so editing
/// one without the other fails the build and names what differs.
/// </remarks>
public sealed partial class PageSchemaContractTests
{
    private static readonly string Contracts = Path.Combine(DraftApiTests.FindRepositoryRoot(), "docs", "contracts");

    private static string Document => File.ReadAllText(Path.Combine(Contracts, "page-schema.md"));

    [Fact]
    public void The_node_type_table_lists_exactly_the_schemas_node_types()
    {
        var documented = Table(Document, "## 2. Node types").Select(row => Unquote(row[0])).ToHashSet(StringComparer.Ordinal);

        // The node types are the root plus whatever the placement lists admit. Other $defs
        // carry a `type` const too — rich text's `doc`, `paragraph`, `text` — but they are not
        // nodes (011 research ED-05).
        var defined = NodeTypes().Keys.Append("root").ToHashSet(StringComparer.Ordinal);

        Assert.True(
            documented.SetEquals(defined),
            $"page-schema.md §2 lists [{string.Join(", ", documented.Order())}] but the schema defines [{string.Join(", ", defined.Order())}].");
    }

    [Fact]
    public void The_node_type_table_lists_exactly_each_types_props()
    {
        var documented = Table(Document, "## 2. Node types")
            .Where(row => Unquote(row[0]) != "root")
            .ToDictionary(
                row => Unquote(row[0]),
                row => Backticked().Matches(row[1]).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        foreach (var (type, props) in NodeTypes())
        {
            Assert.True(documented.TryGetValue(type, out var listed), $"page-schema.md §2 has no row for `{type}`.");
            Assert.True(
                listed.SetEquals(props),
                $"page-schema.md §2 lists `{type}` props [{string.Join(", ", listed.Order())}] but the schema has [{string.Join(", ", props.Order())}].");
        }
    }

    [Fact]
    public void Every_block_type_has_a_valid_and_an_invalid_example()
    {
        // 011 SC-009: a golden file per block type on each side, so no type is added without
        // one. A type counts as covered by any example that uses it.
        var examples = Path.Combine(Contracts, "page-schema", "examples");
        var valid = TypesUsed(Directory.GetFiles(Path.Combine(examples, "valid"), "*.json"));
        var invalid = TypesUsed(Directory.GetFiles(Path.Combine(examples, "invalid"), "*--*.json"));

        Assert.All(NodeTypes().Keys, type =>
        {
            Assert.True(valid.Contains(type), $"No valid example uses `{type}`.");
            Assert.True(invalid.Contains(type), $"No invalid example uses `{type}`.");
        });
    }

    /// <summary>Each node type the placement lists admit, with its schema props.</summary>
    private static Dictionary<string, HashSet<string>> NodeTypes()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Contracts, "page-schema", "v1.schema.json")));
        var defs = schema.RootElement.GetProperty("$defs");

        var types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var list in new[] { "rootChild", "contentChild" })
        {
            foreach (var branch in defs.GetProperty(list).GetProperty("oneOf").EnumerateArray())
            {
                var definition = defs.GetProperty(branch.GetProperty("$ref").GetString()!["#/$defs/".Length..]);
                var properties = definition.GetProperty("properties");
                types[properties.GetProperty("type").GetProperty("const").GetString()!] = properties
                    .GetProperty("props").GetProperty("properties").EnumerateObject()
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.Ordinal);
            }
        }

        return types;
    }

    /// <summary>Every node type any of <paramref name="files"/> uses, parsed or not.</summary>
    private static HashSet<string> TypesUsed(IEnumerable<string> files)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            // Some invalid examples are not JSON at all; a textual scan still finds their types.
            foreach (Match match in NodeTypeValue().Matches(File.ReadAllText(file)))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        return used;
    }

    [Fact]
    public void The_limits_table_matches_what_the_code_enforces()
    {
        var documented = Table(Document, "## 4. Limits").ToDictionary(row => row[0], row => Number(row[1]), StringComparer.Ordinal);

        var enforced = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Document size"] = PageSchema.MaxDocumentBytes,
            ["Nodes per document"] = PageSchema.MaxNodes,
            ["Node depth"] = PageSchema.MaxNodeDepth,
            ["Node id length"] = PageSchema.MaxNodeIdLength,
            ["Slug length"] = PageAddresses.MaxSlugLength,
            ["Address length"] = PageAddresses.MaxPathLength,
            ["Page nesting"] = PageAddresses.MaxDepth,
            ["Pages per site"] = PageAddresses.MaxPagesPerSite,
        };

        Assert.Equal(enforced.OrderBy(e => e.Key), documented.OrderBy(e => e.Key));
    }

    [Fact]
    public void The_node_id_limit_in_the_schema_matches_the_contract()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Contracts, "page-schema", "v1.schema.json")));
        var pattern = schema.RootElement.GetProperty("$defs").GetProperty("nodeId").GetProperty("pattern").GetString()!;

        Assert.EndsWith($"{{1,{PageSchema.MaxNodeIdLength}}}$", pattern, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reserved_segment_table_matches_the_code()
    {
        var documented = Table(Document, "## 5. Addresses and reserved segments").Select(row => Unquote(row[0]));

        Assert.Equal(PageAddresses.ReservedFirstSegments.Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_embedded_schema_is_the_file_in_docs()
    {
        using var embedded = PageSchema.OpenSchema(1);
        using var copy = new MemoryStream();
        embedded.CopyTo(copy);

        Assert.Equal(File.ReadAllBytes(Path.Combine(Contracts, "page-schema", "v1.schema.json")), copy.ToArray());
    }

    [Fact]
    public void Every_example_the_document_lists_exists()
    {
        var listed = Table(Document, "## 8. Examples (golden files)").Select(row => Unquote(row[0]));

        Assert.All(listed, file => Assert.True(
            File.Exists(Path.Combine(Contracts, "page-schema", "examples", file)),
            $"page-schema.md §8 lists {file}, which does not exist."));
    }

    [Fact]
    public void The_drift_check_fires_when_a_node_type_is_missing()
    {
        var edited = Document.Replace("| `component-ref` |", "| `component-reference` |", StringComparison.Ordinal);

        var documented = Table(edited, "## 2. Node types").Select(row => Unquote(row[0]));

        Assert.DoesNotContain("component-ref", documented);
    }

    /// <summary>The body rows of the first table after <paramref name="heading"/>, as cells.</summary>
    internal static List<string[]> Table(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"page-schema.md has no section '{heading}'.");

        var rows = new List<string[]>();
        var inTable = false;
        foreach (var line in markdown[start..].Split('\n').Skip(1).Select(l => l.TrimEnd('\r')))
        {
            if (line.StartsWith('|'))
            {
                inTable = true;
                rows.Add([.. line.Trim('|').Split('|').Select(c => c.Trim())]);
            }
            else if (inTable)
            {
                break;
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }
        }

        // Drop the header row and the |---| separator.
        return [.. rows.Skip(2)];
    }

    private static string Unquote(string cell) => cell.Trim('`');

    private static long Number(string cell) =>
        long.Parse(Digits().Match(cell).Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

    [GeneratedRegex("[0-9][0-9,]*")]
    private static partial Regex Digits();

    [GeneratedRegex("`([A-Za-z]+)`")]
    private static partial Regex Backticked();

    [GeneratedRegex("\"type\"\\s*:\\s*\"([a-z-]+)\"")]
    private static partial Regex NodeTypeValue();
}
