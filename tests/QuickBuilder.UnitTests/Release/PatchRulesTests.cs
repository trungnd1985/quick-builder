extern alias releasetool;

using releasetool::QuickBuilder.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// <c>qb-release patch check</c>: a patch release adds no migration and raises no content schema
/// version, which is what lets an older patch run on its database (019 FR-034a, clarification Q1).
/// </summary>
public sealed class PatchRulesTests : IDisposable
{
    private const string PageSchema = "src/Modules/Pages/QuickBuilder.Pages.Contracts/PageSchema.cs";

    private readonly TempRepository _repo = new();

    public PatchRulesTests() =>
        _repo.Write(PageSchema, "public const int CurrentVersion = 1;\npublic const int CurrentBindingVersion = 1;\n")
            .Write("docs/contracts/page-schema/v1.schema.json", "{}")
            .Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/ItemReads.cs", "class ItemReads {}")
            .Commit("0.2.0").Tag("v0.2.0");

    public void Dispose() => _repo.Dispose();

    private IReadOnlyList<string> Problems() => PatchRules.Check(_repo.Root, "v0.2.0");

    [Fact]
    public void An_ordinary_fix_passes()
    {
        _repo.Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/ItemReads.cs", "class ItemReads { fixed }").Commit();

        Assert.Empty(Problems());
    }

    [Fact]
    public void An_added_migration_fails()
    {
        const string Added = "src/Modules/Cms/QuickBuilder.Cms/Infrastructure/Migrations/20261101000000_AddFix.cs";
        _repo.Write(Added, "up();").Commit();

        Assert.Contains(Problems(), p => p.Contains(Added, StringComparison.Ordinal));
    }

    [Fact]
    public void An_added_schema_version_fails()
    {
        _repo.Write("docs/contracts/page-schema/v2.schema.json", "{}").Commit();

        Assert.Contains(Problems(), p => p.Contains("v2.schema.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public const int CurrentVersion = 2;\npublic const int CurrentBindingVersion = 1;\n")]
    [InlineData("public const int CurrentVersion = 1;\npublic const int CurrentBindingVersion = 2;\n")]
    public void A_raised_version_constant_fails(string text)
    {
        _repo.Write(PageSchema, text).Commit();

        Assert.Contains(Problems(), p => p.Contains(PageSchema, StringComparison.Ordinal));
    }

    [Fact]
    public void An_unchanged_constant_beside_other_edits_passes()
    {
        _repo.Write(PageSchema, "// a comment\npublic const int CurrentVersion = 1;\npublic const int CurrentBindingVersion = 1;\n").Commit();

        Assert.Empty(Problems());
    }
}
