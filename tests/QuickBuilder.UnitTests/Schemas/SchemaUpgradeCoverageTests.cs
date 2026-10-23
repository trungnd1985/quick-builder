using System.Text.Json.Nodes;
using QuickBuilder.Shared.Infrastructure.Schemas;
using QuickBuilder.UnitTests.Rendering;

namespace QuickBuilder.UnitTests.Schemas;

/// <summary>
/// Every versioned contract can bring each older version to the current one, and every step is
/// pinned by golden files (019 contracts/schema-upgrades.md §4, FR-023, FR-024).
/// </summary>
/// <remarks>
/// Every contract is at version 1 today, so the real chains pass vacuously; the rules are proved
/// against a test-only contract laid out in a temporary folder, as a future version 2 would be.
/// </remarks>
public sealed class SchemaUpgradeCoverageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qb-upgrades-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Every chain the product reads stored documents through.</summary>
    public static TheoryData<string> Contracts() => [.. Chains().Keys];

    internal static Dictionary<string, SchemaUpgradeChain> Chains()
    {
        var pages = QuickBuilder.Pages.Infrastructure.Schema.PageSchemaUpgrades.Create();
        return new Dictionary<string, SchemaUpgradeChain>(StringComparer.Ordinal)
        {
            [pages.Pages.Contract] = pages.Pages,
            [pages.Bindings.Contract] = pages.Bindings,
            ["menu"] = QuickBuilder.Pages.Infrastructure.Schema.MenuSchemaUpgrades.Create().Menus,
            ["design-tokens"] = QuickBuilder.Editor.Infrastructure.DesignTokenUpgrades.CreateChain(),
            ["collection-schema"] = QuickBuilder.Cms.Infrastructure.Schema.CollectionSchemaUpgrades.CreateChain(),
        };
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Every_step_of_every_contract_has_golden_files(string contract)
    {
        var chain = Chains()[contract];

        Assert.True(Directory.Exists(RepositoryFiles.Path("docs", "contracts", contract)), $"docs/contracts/{contract} does not exist.");
        Assert.Empty(SchemaUpgradeCoverage.Problems(chain, RepositoryFiles.Path("docs", "contracts")));
    }

    [Fact]
    public void A_missing_golden_folder_is_reported()
    {
        var chain = TestChain();

        var problem = Assert.Single(SchemaUpgradeCoverage.Problems(chain, _root));
        Assert.Contains("upgrades/v1-to-v2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_without_pairs_or_with_an_orphan_is_reported()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "test-contract", "upgrades", "v1-to-v2")).FullName;
        Assert.Contains(SchemaUpgradeCoverage.Problems(TestChain(), _root), p => p.Contains("no golden pair", StringComparison.Ordinal));

        File.WriteAllText(Path.Combine(folder, "a.in.json"), """{"schemaVersion":1,"title":"x"}""");
        Assert.Contains(SchemaUpgradeCoverage.Problems(TestChain(), _root), p => p.Contains("a.in.json has no a.out.json", StringComparison.Ordinal));
    }

    [Fact]
    public void A_complete_folder_passes_and_its_pairs_are_checked()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "test-contract", "upgrades", "v1-to-v2")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.in.json"), """{"schemaVersion":1,"title":"x"}""");
        File.WriteAllText(Path.Combine(folder, "a.out.json"), """{"schemaVersion":2,"heading":"x"}""");

        Assert.Empty(SchemaUpgradeCoverage.Problems(TestChain(), _root));
        Assert.Empty(SchemaUpgradeGolden.Mismatches(TestChain(), _root));
    }

    [Fact]
    public void A_step_whose_output_differs_from_its_golden_file_is_reported()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "test-contract", "upgrades", "v1-to-v2")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.in.json"), """{"schemaVersion":1,"title":"x"}""");
        File.WriteAllText(Path.Combine(folder, "a.out.json"), """{"schemaVersion":2,"heading":"y"}""");

        var mismatch = Assert.Single(SchemaUpgradeGolden.Mismatches(TestChain(), _root));
        Assert.Contains("a.in.json", mismatch, StringComparison.Ordinal);
    }

    private static SchemaUpgradeChain TestChain() => new("test-contract", 2, [new RenameTitle()]);

    private sealed class RenameTitle : ISchemaUpgradeStep
    {
        public int From => 1;

        public JsonObject Upgrade(JsonObject document)
        {
            document["heading"] = document["title"]?.DeepClone();
            document.Remove("title");
            document["schemaVersion"] = 2;
            return document;
        }
    }
}

/// <summary>The golden-file layout rules of schema-upgrades.md §4.</summary>
internal static class SchemaUpgradeCoverage
{
    public static IReadOnlyList<string> Problems(SchemaUpgradeChain chain, string contractsRoot)
    {
        var problems = new List<string>();
        foreach (var step in chain.Steps)
        {
            var name = $"upgrades/v{step.From}-to-v{step.From + 1}";
            var folder = Path.Combine(contractsRoot, chain.Contract, "upgrades", $"v{step.From}-to-v{step.From + 1}");
            if (!Directory.Exists(folder))
            {
                problems.Add($"{chain.Contract}: {name} is missing; every step needs golden files.");
                continue;
            }

            var inputs = Directory.GetFiles(folder, "*.in.json");
            if (inputs.Length == 0)
            {
                problems.Add($"{chain.Contract}: {name} has no golden pair (*.in.json and *.out.json).");
            }

            problems.AddRange(inputs
                .Where(i => !File.Exists(i[..^".in.json".Length] + ".out.json"))
                .Select(i => $"{chain.Contract}: {name}/{Path.GetFileName(i)} has no {Path.GetFileName(i[..^".in.json".Length])}.out.json."));
        }

        return problems;
    }
}

/// <summary>Runs each step on its golden inputs and compares with the expected outputs.</summary>
internal static class SchemaUpgradeGolden
{
    public static IReadOnlyList<string> Mismatches(SchemaUpgradeChain chain, string contractsRoot)
    {
        var mismatches = new List<string>();
        foreach (var step in chain.Steps)
        {
            var folder = Path.Combine(contractsRoot, chain.Contract, "upgrades", $"v{step.From}-to-v{step.From + 1}");
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var input in Directory.GetFiles(folder, "*.in.json").Order(StringComparer.Ordinal))
            {
                var expected = JsonNode.Parse(File.ReadAllText(input[..^".in.json".Length] + ".out.json"));
                var actual = step.Upgrade(JsonNode.Parse(File.ReadAllText(input))!.AsObject());
                if (!JsonNode.DeepEquals(expected, actual))
                {
                    mismatches.Add($"{chain.Contract} v{step.From}: {Path.GetFileName(input)} gave {actual.ToJsonString()}");
                }
            }
        }

        return mismatches;
    }
}
