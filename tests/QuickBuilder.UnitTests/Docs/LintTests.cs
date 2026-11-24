using QuickBuilder.Docs.Lint;
using QuickBuilder.Docs.Settings;
using static QuickBuilder.UnitTests.Docs.CatalogFixtures;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// The lint framework and its checks over a scratch docs tree (026 T009, T020, T045, T046,
/// T054, T069, T072, T076; contracts/qb-docs-cli.md).
/// </summary>
public sealed class LintTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qb-lint-" + Guid.NewGuid().ToString("N"));

    public LintTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs", "operators", ".vitepress"));
        File.WriteAllText(Path.Combine(_root, "QuickBuilder.slnx"), "<Solution />");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private LintTests Page(string path, string text)
    {
        var full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return this;
    }

    private LintContext Context() => new(_root, () => Build(typeof(Good)));

    private List<string> Run(ILintCheck check) => [.. check.Run(Context())];

    [Fact]
    public void The_runner_prints_sorted_findings_and_exits_1()
    {
        Page("docs/operators/index.md", "# Start");
        var error = new StringWriter();

        var exit = LintRunner.Run(Context(), "start-page", TextWriter.Null, error);

        Assert.Equal(1, exit);
        var lines = error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.All(lines[..^1], l => Assert.StartsWith("[start-page] docs/operators/index.md: does not link", l, StringComparison.Ordinal));
        Assert.Equal(lines[..^1].Order(StringComparer.Ordinal), lines[..^1]);
    }

    [Fact]
    public void The_runner_exits_0_when_clean_and_2_for_an_unknown_check()
    {
        Page("docs/operators/index.md", string.Join('\n', StartPageCheck.Targets.Select(t => $"[x]({t})")));

        Assert.Equal(0, LintRunner.Run(Context(), "start-page", TextWriter.Null, TextWriter.Null));
        Assert.Throws<QuickBuilder.Docs.UsageException>(() => LintRunner.Run(Context(), "nope", TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void Nav_pages_finds_missing_pages_and_orphans()
    {
        Page("docs/operators/.vitepress/config.ts", "items: [{ text: 'A', link: '/install' }, { text: 'B', link: '/reverse-proxy/' }]")
            .Page("docs/operators/index.md", "#")
            .Page("docs/operators/install.md", "#")
            .Page("docs/operators/orphan.md", "#")
            .Page("docs/operators/reference/notes/hosting.md", "note");

        var findings = Run(new NavPagesCheck());

        Assert.Contains(findings, f => f.Contains("'/reverse-proxy/', but docs/operators/reverse-proxy/index.md does not exist", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.StartsWith("docs/operators/orphan.md: not in the sidebar", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Contains("notes", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_exist_catches_unknown_keys_in_both_forms()
    {
        Page("docs/operators/install.md", "Set `Good:Flag`, `Good:Names:<n>`, `Good__Names__0` and `Good:Remote:Bucket`.\nBut `Good:Flagg` and Good__Nope=1 are wrong; `Other:Thing` is not ours.");

        var findings = Run(new SettingsExistCheck());

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Contains("'Good:Flagg'", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Contains("'Good:Nope'", StringComparison.Ordinal));
    }

    [Fact]
    public void Notes_must_not_restate_the_table()
    {
        Page("docs/operators/reference/notes/database.md", "| `Good:Flag` | x |\n`Good:Count` defaults to `3`.\nWhy it matters.");

        Assert.Equal(2, Run(new NotesRestateCheck()).Count);
    }

    [Fact]
    public void Example_secrets_must_be_placeholders()
    {
        Page("docs/operators/install.md", "Good__ConnectionString: \"Host=db;Password=hunter2\"\nGood__ConnectionString=change-me-before-real-use\nGood__ConnectionString: ${DB}");

        var finding = Assert.Single(Run(new ExampleSecretsCheck()));
        Assert.Contains("install.md:1", finding, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("## Upgrading from feature 012", true)]
    [InlineData("### Upgrading to feature 023", true)]
    [InlineData("Applies from feature 15 on.", true)]
    [InlineData("Merged behind a feature flag.", false)]
    public void Milestone_references_are_found(string line, bool found)
    {
        Assert.Equal(found, MilestoneReferenceCheck.Matches(line));
    }

    [Fact]
    public void Proxy_pages_pin_the_version_they_were_checked_with()
    {
        Assert.Empty(ProxyVersionCheck.Check("p.md", "nginx", "nginx", "Checked with nginx 1.27.3.\n    image: nginx:1.27.3"));
        Assert.Single(ProxyVersionCheck.Check("p.md", "nginx", "nginx", "Checked with nginx 1.27.3.\n    image: nginx:1.26.0"));
        Assert.Single(ProxyVersionCheck.Check("p.md", "nginx", "nginx", "    image: nginx:1.27.3"));
    }

    [Fact]
    public void Troubleshooting_needs_every_phase_and_symptom()
    {
        var all = TroubleshootingCoverageCheck.Phases.Concat(TroubleshootingCoverageCheck.Symptoms).ToList();
        Page(TroubleshootingCoverageCheck.PagePath, string.Join('\n', all.Where(a => a != "migrations-pending").Select(a => $"## {a} {{#{a}}}")));

        var finding = Assert.Single(Run(new TroubleshootingCoverageCheck()));
        Assert.Contains("{#migrations-pending}", finding, StringComparison.Ordinal);
        Assert.Contains("waiting-for-database", TroubleshootingCoverageCheck.Phases);
    }

    [Fact]
    public void Security_items_need_check_verify_and_fix()
    {
        var items = SecurityCoverageCheck.Items.Select(i =>
            i == "tls"
                ? "## TLS {#tls}\n**Check** x\n**Fix** z\n"
                : $"## {i} {{#{i}}}\n**Check** x\n**Verify** y\n**Fix** z\n");
        Page(SecurityCoverageCheck.PagePath, string.Concat(items.Skip(1)) + items.First());

        var findings = Run(new SecurityCoverageCheck());

        Assert.Contains(findings, f => f.Contains("{#tls} has no **Verify**", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Contains("{#report-a-vulnerability}", StringComparison.Ordinal));
        Assert.Equal(2, findings.Count);
    }

    [Fact]
    public void Nav_links_map_to_pages()
    {
        Assert.Equal("docs/operators/index.md", NavPagesCheck.PageFor("/"));
        Assert.Equal("docs/operators/install.md", NavPagesCheck.PageFor("/install"));
        Assert.Equal("docs/operators/reverse-proxy/index.md", NavPagesCheck.PageFor("/reverse-proxy/"));
        Assert.Equal("docs/operators/reference/configuration.md", NavPagesCheck.PageFor("/reference/configuration#x"));
    }

    [Fact]
    public void Setting_tokens_lose_placeholders_and_indexes()
    {
        Assert.Equal("Hosting:KnownProxies", SettingsExistCheck.Normalise("Hosting:KnownProxies:<n>"));
        Assert.Equal("Hosting:KnownProxies", SettingsExistCheck.Normalise("Hosting:KnownProxies:0"));
        Assert.Equal("Catalog", nameof(Catalog));
    }
}
