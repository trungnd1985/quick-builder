using QuickBuilder.Docs.Settings;
using QuickBuilder.Docs.Snippets;

namespace QuickBuilder.Docs.Lint;

/// <summary>What every check reads: the repository, its operator pages and, when needed, the catalog.</summary>
internal sealed class LintContext(string repositoryRoot, Func<Catalog>? catalog = null)
{
    private readonly Lazy<Catalog> _catalog = new(catalog ?? (() => CatalogBuilder.BuildForProduct(repositoryRoot)));

    public string RepositoryRoot { get; } = repositoryRoot;

    public string DocsRoot => Path.Combine(RepositoryRoot, SnippetCommands.DocsRoot);

    public Catalog Catalog => _catalog.Value;

    /// <summary>Operator pages as repository-relative paths with <c>/</c>, sorted.</summary>
    public IReadOnlyList<string> Pages => [.. SnippetCommands.Pages(RepositoryRoot).Select(Relative)];

    public string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot, relative));

    public bool Exists(string relative) => File.Exists(Path.Combine(RepositoryRoot, relative));

    public string Relative(string path) => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');
}

/// <summary>One documentation rule (026 contracts/qb-docs-cli.md, <c>lint</c> checks).</summary>
internal interface ILintCheck
{
    string Name { get; }

    /// <summary>Findings, one line each: <c>&lt;where&gt;: &lt;problem&gt; — &lt;fix&gt;</c>.</summary>
    IEnumerable<string> Run(LintContext context);
}

/// <summary><c>lint [--check &lt;name&gt;]</c>.</summary>
internal static class LintRunner
{
    public static IReadOnlyList<ILintCheck> Checks { get; } =
    [
        new NavPagesCheck(),
        new StartPageCheck(),
        new SettingsExistCheck(),
        new NotesRestateCheck(),
        new TroubleshootingCoverageCheck(),
        new SecurityCoverageCheck(),
        new MilestoneReferenceCheck(),
        new SnippetMarkersCheck(),
        new ExampleSecretsCheck(),
        new ProxyVersionCheck(),
        new NoOldPathsCheck(),
        new UpgradeNotesCurrentCheck(),
    ];

    public static int Run(string repositoryRoot, string? only, TextWriter output, TextWriter error) =>
        Run(new LintContext(repositoryRoot), only, output, error);

    internal static int Run(LintContext context, string? only, TextWriter output, TextWriter error)
    {
        var checks = only is null ? Checks : Checks.Where(c => c.Name == only).ToList();
        if (checks.Count == 0)
        {
            throw new UsageException($"unknown check '{only}' — use one of {string.Join(", ", Checks.Select(c => c.Name))}.");
        }

        var findings = checks.SelectMany(c => c.Run(context).Select(f => $"[{c.Name}] {f}")).Order(StringComparer.Ordinal).ToList();
        foreach (var finding in findings)
        {
            error.WriteLine(finding);
        }

        if (findings.Count > 0)
        {
            error.WriteLine($"{findings.Count} finding(s). See docs/contributing.md#operator-docs.");
            return Program.Failed;
        }

        output.WriteLine($"Operator docs pass {checks.Count} check(s).");
        return Program.Pass;
    }
}
