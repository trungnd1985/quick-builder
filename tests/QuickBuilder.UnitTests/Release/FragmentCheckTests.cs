extern alias releasetool;

using releasetool::QuickBuilder.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// <c>qb-release fragment check</c> and <c>archive</c> (019 contracts/release-notes.md §4,
/// release-pipeline.md §5).
/// </summary>
public sealed class FragmentCheckTests : IDisposable
{
    private readonly TempRepository _repo = new();

    public FragmentCheckTests() =>
        _repo.Props("0.2.0")
            .Write("docs/configuration.md", "| `A:B` | x | y |\n")
            .Write("docs/contracts/page-schema/v1.schema.json", "{}")
            .Write("docs/contracts/page-schema/examples/valid/a.json", "{}")
            .Write("docker-compose.yml", "services: {}\n")
            .Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/ItemReads.cs", "class X {}")
            .Write("docs/releases/unreleased/.gitkeep", string.Empty)
            .Commit("main").Tag("base");

    public void Dispose() => _repo.Dispose();

    private IReadOnlyList<string> Check() => FragmentCheck.Check(_repo.Root, "base");

    [Theory]
    [InlineData("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/Migrations/20261101000000_AddThings.cs")]
    [InlineData("docs/configuration.md")]
    [InlineData("docs/contracts/page-schema/v1.schema.json")]
    [InlineData("docker-compose.yml")]
    public void A_change_operators_must_hear_about_needs_a_fragment(string path)
    {
        _repo.Write(path, "changed").Commit();

        Assert.Contains(Check(), p => p.Contains(path, StringComparison.Ordinal));

        _repo.Write("docs/releases/unreleased/7-thing.md", "---\nissue: 7\nkind: changed\nbreaking: false\n---\nA change.\n").Commit();
        Assert.Empty(Check());
    }

    [Fact]
    public void Ordinary_code_and_contract_examples_need_none()
    {
        _repo.Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/ItemReads.cs", "class X { y }")
            .Write("docs/contracts/page-schema/examples/valid/a.json", "{ }")
            .Commit();

        Assert.Empty(Check());
    }

    [Fact]
    public void An_added_fragment_must_itself_parse()
    {
        _repo.Write("docs/releases/unreleased/7-thing.md", "not a fragment").Commit();

        Assert.Contains(Check(), p => p.Contains("7-thing.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Archive_moves_the_fragments_and_bumps_the_prefix_to_the_next_minor()
    {
        _repo.Write("docs/releases/unreleased/7-thing.md", "---\nissue: 7\nkind: changed\nbreaking: false\n---\nA change.\n")
            .Write("docs/releases/unreleased/7-visible-changes.json", """{ "issue": 7, "reason": "r", "pages": [ { "host": "*", "path": "/" } ] }""")
            .Commit();

        ReleaseArchive.Archive(_repo.Root, releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion.Parse("0.2.0"));

        Assert.True(File.Exists(Path.Combine(_repo.Root, "docs", "releases", "0.2.0", "7-thing.md")));
        Assert.True(File.Exists(Path.Combine(_repo.Root, "docs", "releases", "0.2.0", "7-visible-changes.json")));
        Assert.False(File.Exists(Path.Combine(_repo.Root, "docs", "releases", "unreleased", "7-thing.md")));
        Assert.True(File.Exists(Path.Combine(_repo.Root, "docs", "releases", "unreleased", ".gitkeep")));
        Assert.Contains("<VersionPrefix>0.3.0</VersionPrefix>", File.ReadAllText(Path.Combine(_repo.Root, "Directory.Build.props")), StringComparison.Ordinal);
    }
}
