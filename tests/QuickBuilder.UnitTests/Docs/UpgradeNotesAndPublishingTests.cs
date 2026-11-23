extern alias releasetool;

using QuickBuilder.Docs.Publishing;
using QuickBuilder.Docs.UpgradeNotes;
using SemanticVersion = releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// The upgrade notes page and the published site's version list (026 T053, T080; FR-028,
/// FR-036 – FR-039; data-model §4).
/// </summary>
public sealed class UpgradeNotesAndPublishingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qb-notes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Fragment(string folder, int issue, string manual = "", string added = "", bool breaking = false)
    {
        var directory = Path.Combine(_root, "docs", "releases", folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{issue}-change.md"), $"""
            ---
            issue: {issue}
            kind: added
            breaking: {(breaking ? "true" : "false")}
            config:
              added: [{added}]
              changed: []
              removed: []
            background: []
            caches: []
            manual: [{manual}]
            ---
            Something changed.
            """);
    }

    [Fact]
    public void Notes_are_newest_first_with_an_anchor_each_and_only_upgrade_sections()
    {
        Fragment("0.1.0", 1, manual: "\"Reload the admin.\"");
        Fragment("unreleased", 2, added: "Forms:RateLimit:PerMinute");

        var page = UpgradeNotesGenerator.Render(_root, SemanticVersion.Parse("0.2.0"));

        Assert.True(page.IndexOf("## QuickBuilder 0.2.0", StringComparison.Ordinal) < page.IndexOf("## QuickBuilder 0.1.0", StringComparison.Ordinal));
        Assert.Contains("<a id=\"0-2-0\"></a>", page, StringComparison.Ordinal);
        Assert.Contains("- Reload the admin. (#1)", page, StringComparison.Ordinal);
        Assert.Contains("- Added [`Forms:RateLimit:PerMinute`](reference/configuration.md#forms-ratelimit-perminute) (#2)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("## Highlights", page, StringComparison.Ordinal);
        Assert.Contains("### Caches\n\nNone.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Through_leaves_out_later_releases()
    {
        Fragment("0.1.0", 1);
        Fragment("0.2.0", 2);

        var page = UpgradeNotesGenerator.Render(_root, SemanticVersion.Parse("0.1.0"));

        Assert.Contains("QuickBuilder 0.1.0", page, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickBuilder 0.2.0", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_fragment_fails_with_its_message()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs", "releases", "unreleased"));
        File.WriteAllText(Path.Combine(_root, "docs", "releases", "unreleased", "3-bad.md"), "no front matter");
        var error = new StringWriter();

        var exit = UpgradeNotesGenerator.Run(_root, "0.2.0", TextWriter.Null, error);

        Assert.Equal(1, exit);
        Assert.Contains("front matter", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_is_inserted_then_replaced_and_lines_sort_by_number()
    {
        var day = new DateOnly(2027, 1, 10);
        var lines = VersionsManifest.Upsert([], SemanticVersion.Parse("0.9.0"), day);
        lines = VersionsManifest.Upsert(lines, SemanticVersion.Parse("0.10.0"), day);
        lines = VersionsManifest.Upsert(lines, SemanticVersion.Parse("0.9.1"), day.AddDays(1));

        Assert.Equal(["0.10", "0.9"], lines.Select(l => l.Line));
        Assert.Equal("0.9.1", lines.Single(l => l.Line == "0.9").Release);
        Assert.Equal("2027-01-11", lines.Single(l => l.Line == "0.9").Published);
    }

    [Fact]
    public void Pre_releases_are_never_published()
    {
        Assert.Throws<ArgumentException>(() => VersionsManifest.Upsert([], SemanticVersion.Parse("0.3.0-rc.1"), new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Prepare_replaces_only_its_line_and_moves_the_root_only_when_highest()
    {
        var site = Path.Combine(_root, "site");
        var build = Path.Combine(_root, "dist");
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, "index.html"), "new");
        Directory.CreateDirectory(Path.Combine(site, "0.2"));
        File.WriteAllText(Path.Combine(site, "0.2", "index.html"), "old 0.2");
        Directory.CreateDirectory(Path.Combine(site, "0.3"));
        File.WriteAllText(Path.Combine(site, "0.3", "stale.html"), "old 0.3");
        File.WriteAllText(Path.Combine(site, "index.html"), VersionsManifest.RedirectPage("0.3"));

        var exit = VersionsManifest.Prepare(site, build, "0.2.1", highest: false, new DateOnly(2027, 1, 1), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exit);
        Assert.Equal("new", File.ReadAllText(Path.Combine(site, "0.2", "index.html")));
        Assert.True(File.Exists(Path.Combine(site, "0.3", "stale.html")));
        Assert.Contains("./0.3/", File.ReadAllText(Path.Combine(site, "index.html")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(site, ".nojekyll")));

        VersionsManifest.Prepare(site, build, "0.4.0", highest: true, new DateOnly(2027, 2, 1), TextWriter.Null, TextWriter.Null);
        Assert.Contains("./0.4/", File.ReadAllText(Path.Combine(site, "index.html")), StringComparison.Ordinal);
        Assert.Contains("\"line\": \"0.4\"", File.ReadAllText(Path.Combine(site, "versions.json")), StringComparison.Ordinal);
    }
}
