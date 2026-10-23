extern alias releasetool;

using releasetool::QuickBuilder.Release;
using SemanticVersion = releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// <c>qb-release notes</c>: release and upgrade notes assembled from fragments and from what git
/// shows (019 contracts/release-notes.md §1, §3; FR-032 – FR-034, FR-012a).
/// </summary>
public sealed class NotesAssemblerTests : IDisposable
{
    private const string Unreleased = "docs/releases/unreleased";
    private const string Config = "docs/configuration.md";

    private readonly TempRepository _repo = new();

    public NotesAssemblerTests()
    {
        var template = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "releases", "TEMPLATE.md"));
        _repo.Props("0.2.0")
            .Write("docs/releases/TEMPLATE.md", template)
            .Write(Config, "| Key | Default | Meaning |\n|---|---|---|\n| `Database:AutoMigrate` | `true` | x |\n")
            .Write("src/Modules/Pages/QuickBuilder.Pages.Contracts/PageSchema.cs", "public const int CurrentVersion = 1;\n")
            .Write($"{Unreleased}/.gitkeep", string.Empty)
            .Commit("0.1.0").Tag("v0.1.0");
    }

    public void Dispose() => _repo.Dispose();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private string Assemble(string version = "0.2.0", bool patch = false) =>
        NotesAssembler.Assemble(_repo.Root, SemanticVersion.Parse(version), "v0.1.0", ["0.1.0"], patch, new DateOnly(2026, 11, 2));

    private TempRepository Fragment(string name, string front, string body = "Something changed.") =>
        _repo.Write($"{Unreleased}/{name}", $"---\n{front}\n---\n{body}\n");

    [Fact]
    public void Every_section_is_there_and_empty_ones_say_none()
    {
        Fragment("5-thing.md", "issue: 5\nkind: added\nbreaking: false");

        var notes = Assemble();

        string[] sections =
        [
            "# QuickBuilder 0.2.0", "## Highlights", "## Changes", "### Added", "### Changed", "### Fixed", "### Security",
            "## Upgrade notes", "### Breaking changes", "### Manual steps", "### Configuration", "### Database migrations",
            "### Content schema versions", "### Background work after upgrading", "### Caches", "### Visible changes",
            "## Supported upgrade path", "## Images", "## Known issues",
        ];
        var positions = sections.Select(s => notes.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0, notes));
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("Released 2026-11-02", notes, StringComparison.Ordinal);
        Assert.Contains("Something changed. (#5)", notes, StringComparison.Ordinal);
        Assert.Contains("None.", notes, StringComparison.Ordinal);
        Assert.Contains("{{CANDIDATE}}", notes, StringComparison.Ordinal);
        Assert.Contains("0.1.0", notes[notes.IndexOf("## Supported upgrade path", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void Migrations_added_since_the_previous_release_are_listed_by_module()
    {
        Fragment("5-tables.md", "issue: 5\nkind: added\nbreaking: false");
        _repo.Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/Migrations/20261101000000_AddThings.cs", "up")
            .Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/Migrations/20261101000000_AddThings.Designer.cs", "model")
            .Write("src/Shared/QuickBuilder.Shared.Infrastructure/Migrations/Platform/Migrations/20261102000000_AddMore.cs", "up")
            .Commit();

        var notes = Assemble();

        Assert.Contains("- Cms: `AddThings`", notes, StringComparison.Ordinal);
        Assert.Contains("- Platform: `AddMore`", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("Designer", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void A_raised_schema_version_is_listed()
    {
        Fragment("5-v2.md", "issue: 5\nkind: changed\nbreaking: false");
        _repo.Write("src/Modules/Pages/QuickBuilder.Pages.Contracts/PageSchema.cs", "public const int CurrentVersion = 2;\n")
            .Write("docs/contracts/page-schema/v2.schema.json", "{}")
            .Commit();

        Assert.Contains("page-schema: version 2", Assemble(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fragment_lists_reach_their_sections()
    {
        Fragment("5-a.md", "issue: 5\nkind: security\nbreaking: false\nmanual: [\"Set the new password.\"]\nbackground: [\"Items are indexed again.\"]\ncaches: [\"Pages render once more.\"]", "Closed a hole.");

        var notes = Assemble();

        Assert.Contains("- Set the new password. (#5)", notes, StringComparison.Ordinal);
        Assert.Contains("- Items are indexed again. (#5)", notes, StringComparison.Ordinal);
        Assert.Contains("- Pages render once more. (#5)", notes, StringComparison.Ordinal);
        var security = notes[notes.IndexOf("### Security", StringComparison.Ordinal)..notes.IndexOf("## Upgrade notes", StringComparison.Ordinal)];
        Assert.Contains("Closed a hole. (#5)", security, StringComparison.Ordinal);
    }

    [Fact]
    public void Visible_changes_are_listed_from_the_allow_list()
    {
        Fragment("42-send.md", "issue: 42\nkind: changed\nbreaking: false");
        _repo.Write($"{Unreleased}/42-visible-changes.json", """{ "issue": 42, "reason": "Buttons read Send.", "pages": [ { "host": "*", "path": "/contact" } ] }""");

        var notes = Assemble();

        Assert.Contains("Buttons read Send. (#42): `*/contact`", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "issue": 42, "pages": [ { "host": "*", "path": "/x" } ] }""")]
    [InlineData("""{ "issue": 42, "reason": "r", "pages": [] }""")]
    [InlineData("not json")]
    public void A_malformed_allow_list_is_refused(string json)
    {
        Fragment("42-send.md", "issue: 42\nkind: changed\nbreaking: false");
        _repo.Write($"{Unreleased}/42-visible-changes.json", json);

        Assert.Contains("42-visible-changes.json", Assert.Throws<ReleaseRuleException>(() => Assemble()).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    [InlineData("1.1.0")]
    public void A_breaking_change_outside_a_new_major_is_refused(string version)
    {
        _repo.Props(version).Commit();
        Fragment("5-break.md", "issue: 5\nkind: changed\nbreaking: true");

        Assert.Contains("breaking", Assert.Throws<ReleaseRuleException>(() => Assemble(version)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_breaking_change_in_a_new_major_is_listed()
    {
        Fragment("5-break.md", "issue: 5\nkind: changed\nbreaking: true", "Removed the old setting.");

        var notes = Assemble("1.0.0");

        var breaking = notes[notes.IndexOf("### Breaking changes", StringComparison.Ordinal)..notes.IndexOf("### Manual steps", StringComparison.Ordinal)];
        Assert.Contains("Removed the old setting. (#5)", breaking, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_keys_must_match_what_the_configuration_guide_gained_and_lost()
    {
        _repo.Write(Config, "| Key | Default | Meaning |\n|---|---|---|\n| `Releases:Channel` | `stable` | x |\n").Commit();
        Fragment("5-config.md", "issue: 5\nkind: added\nbreaking: false\nconfig:\n  added: [Releases:Channel]\n  removed: []");

        var error = Assert.Throws<ReleaseRuleException>(() => Assemble());
        Assert.Contains("Database:AutoMigrate", error.Message, StringComparison.Ordinal);

        Fragment("6-config.md", "issue: 6\nkind: changed\nbreaking: false\nconfig:\n  removed: [Database:AutoMigrate]");
        var notes = Assemble();
        Assert.Contains("`Releases:Channel` (added)", notes, StringComparison.Ordinal);
        Assert.Contains("`Database:AutoMigrate` (removed)", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Stored_data_changes_without_any_fragment_are_refused()
    {
        _repo.Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/Migrations/20261101000000_AddThings.cs", "up").Commit();

        Assert.Contains("fragment", Assert.Throws<ReleaseRuleException>(() => Assemble()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_patch_that_adds_a_migration_is_refused()
    {
        Fragment("5-fix.md", "issue: 5\nkind: fixed\nbreaking: false");
        _repo.Write("src/Modules/Cms/QuickBuilder.Cms/Infrastructure/Migrations/20261101000000_AddFix.cs", "up").Commit();

        Assert.Contains("patch", Assert.Throws<ReleaseRuleException>(() => Assemble("0.1.1", patch: true)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_template_missing_a_section_is_refused()
    {
        var template = File.ReadAllText(Path.Combine(_repo.Root, "docs", "releases", "TEMPLATE.md"));
        _repo.Write("docs/releases/TEMPLATE.md", template.Replace("{{knownIssues}}", string.Empty, StringComparison.Ordinal));
        Fragment("5-thing.md", "issue: 5\nkind: added\nbreaking: false");

        Assert.Contains("knownIssues", Assert.Throws<ReleaseRuleException>(() => Assemble()).Message, StringComparison.Ordinal);
    }
}
