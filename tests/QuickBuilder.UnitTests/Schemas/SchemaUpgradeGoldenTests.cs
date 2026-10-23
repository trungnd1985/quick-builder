using QuickBuilder.UnitTests.Rendering;

namespace QuickBuilder.UnitTests.Schemas;

/// <summary>
/// Each upgrade step turns its golden inputs into exactly its golden outputs
/// (019 contracts/schema-upgrades.md §4, FR-024).
/// </summary>
/// <remarks>
/// Passes vacuously while every contract is at version 1; <see cref="SchemaUpgradeCoverageTests"/>
/// proves the comparison fires on a test-only contract.
/// </remarks>
public sealed class SchemaUpgradeGoldenTests
{
    [Theory]
    [MemberData(nameof(SchemaUpgradeCoverageTests.Contracts), MemberType = typeof(SchemaUpgradeCoverageTests))]
    public void Every_step_matches_its_golden_files(string contract)
    {
        var chain = SchemaUpgradeCoverageTests.Chains()[contract];

        Assert.Empty(SchemaUpgradeGolden.Mismatches(chain, RepositoryFiles.Path("docs", "contracts")));
    }
}
