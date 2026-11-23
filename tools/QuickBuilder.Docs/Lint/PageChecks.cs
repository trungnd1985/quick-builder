using System.Text.RegularExpressions;
using QuickBuilder.Shared.Infrastructure.Startup;

namespace QuickBuilder.Docs.Lint;

/// <summary>
/// <c>nav-pages</c>: every sidebar link has a page and every page is in the sidebar
/// (FR-001, FR-003). Hand-written notes are inlined into the reference and need no entry.
/// </summary>
internal sealed partial class NavPagesCheck : ILintCheck
{
    public const string ConfigPath = "docs/operators/.vitepress/config.ts";

    public string Name => "nav-pages";

    public IEnumerable<string> Run(LintContext context)
    {
        if (!context.Exists(ConfigPath))
        {
            yield return $"{ConfigPath}: missing — the site has no sidebar.";
            yield break;
        }

        var links = Link().Matches(context.Read(ConfigPath)).Select(m => m.Groups["link"].Value).Distinct().ToList();
        var linked = new HashSet<string>(StringComparer.Ordinal) { "docs/operators/index.md" };
        foreach (var link in links)
        {
            var page = PageFor(link);
            if (!context.Exists(page))
            {
                yield return $"{ConfigPath}: the sidebar links '{link}', but {page} does not exist — add the page or remove the link.";
            }

            linked.Add(page);
        }

        foreach (var page in context.Pages.Where(p => !p.StartsWith("docs/operators/reference/notes/", StringComparison.Ordinal)))
        {
            if (!linked.Contains(page))
            {
                yield return $"{page}: not in the sidebar — add it to {ConfigPath} so readers can find it.";
            }
        }
    }

    /// <summary><c>/install</c> → <c>docs/operators/install.md</c>; <c>/reverse-proxy/</c> → its <c>index.md</c>.</summary>
    internal static string PageFor(string link)
    {
        var path = link.Split('#')[0].Trim('/');
        return path.Length == 0
            ? "docs/operators/index.md"
            : link.Split('#')[0].EndsWith('/')
                ? $"docs/operators/{path}/index.md"
                : $"docs/operators/{path}.md";
    }

    [GeneratedRegex(@"link:\s*'(?<link>/[^']*)'", RegexOptions.CultureInvariant)]
    private static partial Regex Link();
}

/// <summary><c>start-page</c>: the start page links every operator task (FR-001, SC-009).</summary>
internal sealed class StartPageCheck : ILintCheck
{
    public const string StartPage = "docs/operators/index.md";

    public static IReadOnlyList<string> Targets { get; } =
    [
        "install.md", "configure.md", "reverse-proxy/index.md", "backup-restore.md", "upgrade.md",
        "troubleshooting.md", "security-checklist.md", "reference/configuration.md",
    ];

    public string Name => "start-page";

    public IEnumerable<string> Run(LintContext context)
    {
        if (!context.Exists(StartPage))
        {
            yield return $"{StartPage}: missing — every task starts here.";
            yield break;
        }

        var text = context.Read(StartPage);
        foreach (var target in Targets.Where(t => !text.Contains($"]({t}", StringComparison.Ordinal) && !text.Contains($"](./{t}", StringComparison.Ordinal)))
        {
            yield return $"{StartPage}: does not link {target} — link every task from the start page.";
        }
    }
}

/// <summary>Required anchors on a page, written <c>{#anchor}</c> after a heading.</summary>
internal static partial class Anchors
{
    public static IReadOnlySet<string> In(string text) =>
        Anchor().Matches(text).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

    public static string Kebab(string pascal) => PascalBoundary().Replace(pascal, "$1-$2").ToLowerInvariant();

    [GeneratedRegex(@"\{#(?<id>[a-z0-9-]+)\}|id=""(?<id>[a-z0-9-]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex Anchor();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex PascalBoundary();
}

/// <summary>
/// <c>troubleshooting-coverage</c>: an entry for every startup phase the readiness probe can
/// report, every startup refusal and every FR-030 symptom (FR-029 – FR-031, research OD-18).
/// </summary>
internal sealed class TroubleshootingCoverageCheck : ILintCheck
{
    public const string PagePath = "docs/operators/troubleshooting.md";

    public static IReadOnlyList<string> Symptoms { get; } =
    [
        "container-does-not-start", "configuration-error", "database-unreachable", "host-shows-not-found",
        "admin-unreachable", "upload-rejected", "email-not-sent", "sign-in-throttled",
        "sign-in-refused-over-http", "stale-content", "database-newer", "database-too-old",
    ];

    /// <summary>The readiness phases, from the product itself, so a new phase needs an entry.</summary>
    public static IReadOnlyList<string> Phases { get; } =
        [.. Enum.GetNames<StartupPhase>().Where(n => n != nameof(StartupPhase.Ready)).Select(Anchors.Kebab)];

    public string Name => "troubleshooting-coverage";

    public IEnumerable<string> Run(LintContext context)
    {
        if (!context.Exists(PagePath))
        {
            yield return $"{PagePath}: missing.";
            yield break;
        }

        var present = Anchors.In(context.Read(PagePath));
        foreach (var anchor in Phases.Concat(Symptoms).Where(a => !present.Contains(a)))
        {
            yield return $"{PagePath}: no entry {{#{anchor}}} — add a section for it with What you see, Why and Fix.";
        }
    }
}

/// <summary>
/// <c>security-coverage</c>: every checklist item exists and says what to check, how to
/// verify it and where the fix is (FR-033 – FR-035).
/// </summary>
internal sealed partial class SecurityCoverageCheck : ILintCheck
{
    public const string PagePath = "docs/operators/security-checklist.md";

    public static IReadOnlyList<string> Items { get; } =
    [
        "tls", "trusted-proxies", "admin-host-names", "secrets", "database-not-exposed", "media-not-exposed",
        "backups", "updates", "personal-data-in-logs", "outbound-features",
    ];

    public const string Report = "report-a-vulnerability";

    private static readonly string[] Parts = ["**Check**", "**Verify**", "**Fix**"];

    public string Name => "security-coverage";

    public IEnumerable<string> Run(LintContext context)
    {
        if (!context.Exists(PagePath))
        {
            yield return $"{PagePath}: missing.";
            yield break;
        }

        var text = context.Read(PagePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var sections = Heading().Split(text);
        foreach (var item in Items)
        {
            var section = sections.FirstOrDefault(s => s.Contains($"{{#{item}}}", StringComparison.Ordinal));
            if (section is null)
            {
                yield return $"{PagePath}: no item {{#{item}}} — add it.";
                continue;
            }

            foreach (var part in Parts.Where(p => !section.Contains(p, StringComparison.Ordinal)))
            {
                yield return $"{PagePath}: item {{#{item}}} has no {part} — every item says what to check, how to verify it and where the fix is.";
            }
        }

        if (!text.Contains($"{{#{Report}}}", StringComparison.Ordinal))
        {
            yield return $"{PagePath}: no {{#{Report}}} section — say how to report a vulnerability.";
        }
    }

    [GeneratedRegex(@"(?=^## )", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Heading();
}

/// <summary><c>milestone-reference</c>: no upgrade text in terms of feature milestones (FR-044).</summary>
internal sealed partial class MilestoneReferenceCheck : ILintCheck
{
    public string Name => "milestone-reference";

    public IEnumerable<string> Run(LintContext context)
    {
        foreach (var page in context.Pages)
        {
            var lines = context.Read(page).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (Milestone().IsMatch(lines[i]))
                {
                    yield return $"{page}:{i + 1}: refers to a feature milestone as an upgrade point — operators upgrade between releases; use the release's upgrade notes.";
                }
            }
        }
    }

    internal static bool Matches(string line) => Milestone().IsMatch(line);

    [GeneratedRegex(@"upgrading (from|to) feature|from feature \d+|to feature \d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Milestone();
}

/// <summary><c>no-old-paths</c>: nothing links the old operator files (FR-004, SC-010).</summary>
internal sealed class NoOldPathsCheck : ILintCheck
{
    private static readonly string[] OldPaths = ["docs/operations.md", "docs/configuration.md"];

    private static readonly string[] Skipped = ["specs/", "docs/releases/0.1.0/", ".git/", "node_modules/", "/bin/", "/obj/", ".vitepress/"];

    private static readonly string[] Extensions = [".md", ".cs", ".yml", ".yaml", ".json", ".ts", ".vue", ".props", ".csproj", ".ps1", ".sh"];

    public string Name => "no-old-paths";

    public IEnumerable<string> Run(LintContext context)
    {
        foreach (var file in Directory.EnumerateFiles(context.RepositoryRoot, "*", SearchOption.AllDirectories))
        {
            var relative = context.Relative(file);
            if (Skipped.Any(s => relative.StartsWith(s, StringComparison.Ordinal) || relative.Contains(s, StringComparison.Ordinal))
                || !Extensions.Contains(Path.GetExtension(file))
                || relative.StartsWith("tools/QuickBuilder.Docs/", StringComparison.Ordinal)
                || relative.StartsWith("tools/QuickBuilder.Release/ConfigurationKeys.cs", StringComparison.Ordinal)
                || relative.StartsWith("tests/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var old in OldPaths.Where(o => text.Contains(o, StringComparison.Ordinal)))
            {
                yield return $"{relative}: links {old}, which moved to docs/operators/ — link the operator page instead.";
            }
        }
    }
}
