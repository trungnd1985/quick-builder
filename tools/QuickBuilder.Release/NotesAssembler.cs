using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Release;

/// <summary>
/// <c>notes</c>: a release's notes from <c>docs/releases/TEMPLATE.md</c>, the fragments in
/// <c>docs/releases/unreleased/</c>, and what git shows since the previous release
/// (019 contracts/release-notes.md §1, §3; FR-032 – FR-034; research RL-16).
/// </summary>
/// <remarks>
/// People write what a change means; the tool derives the facts (migrations, schema versions)
/// so they cannot be forgotten, and refuses notes that break a rule.
/// </remarks>
internal static partial class NotesAssembler
{
    public const string Template = "docs/releases/TEMPLATE.md";
    public const string Unreleased = "docs/releases/unreleased";

    /// <summary>Replaced by the image reference and digest when the candidate is approved.</summary>
    public const string Candidate = "{{CANDIDATE}}";

    private const string None = "None.";

    private static readonly string[] Markers =
    [
        "version", "date", "highlights", "added", "changed", "fixed", "security", "breaking", "manual",
        "configuration", "migrations", "schemas", "background", "caches", "visible", "upgradePath", "images", "knownIssues", "docs",
    ];

    /// <param name="repositoryRoot">The repository, at the commit being released.</param>
    /// <param name="version">The release.</param>
    /// <param name="previous">The previous release's tag (or the pre-0.1.0 baseline).</param>
    /// <param name="sources">The releases this one's upgrade gate starts from.</param>
    /// <param name="patch">Whether this is a patch release (FR-034a).</param>
    /// <param name="date">The release date.</param>
    public static string Assemble(string repositoryRoot, SemanticVersion version, string previous, IReadOnlyList<string> sources, bool patch, DateOnly date)
    {
        var templatePath = Path.Combine(repositoryRoot, Template);
        if (!File.Exists(templatePath))
        {
            throw new ReleaseRuleException($"{Template} is missing.");
        }

        var template = File.ReadAllText(templatePath);
        var missing = Markers.Where(m => !template.Contains($"{{{{{m}}}}}", StringComparison.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            throw new ReleaseRuleException($"{Template} is missing the section(s) {string.Join(", ", missing.Select(m => "{{" + m + "}}"))} (contracts/release-notes.md §1).");
        }

        var unreleased = Path.Combine(repositoryRoot, Unreleased);
        var fragments = Directory.Exists(unreleased)
            ? Directory.EnumerateFiles(unreleased, "*.md").Select(f => Fragment.Parse(f, File.ReadAllText(f))).OrderBy(f => f.Issue).ThenBy(f => f.File, StringComparer.Ordinal).ToList()
            : [];
        var visible = VisibleChanges.Load(unreleased);

        var core = new SemanticVersion(version.Major, version.Minor, version.Patch);
        var newMajor = core is { Major: >= 1, Minor: 0, Patch: 0 };
        var breaking = fragments.Where(f => f.Breaking).ToList();
        if (breaking.Count > 0 && !newMajor)
        {
            throw new ReleaseRuleException(
                $"{string.Join(", ", breaking.Select(f => Path.GetFileName(f.File)))}: a breaking change waits for a new major version; {version} is not one (FR-034, FR-001a).");
        }

        var hasPrevious = Git.Exists(repositoryRoot, previous);
        var migrations = hasPrevious ? Migrations(repositoryRoot, previous) : [];
        var schemas = hasPrevious ? SchemaRaises(repositoryRoot, previous) : [];
        if ((migrations.Count > 0 || schemas.Count > 0) && fragments.Count == 0)
        {
            throw new ReleaseRuleException("This release changes stored data but has no release-note fragment in docs/releases/unreleased/ (contracts/release-notes.md §3 rule 4).");
        }

        if (patch && hasPrevious)
        {
            var problems = PatchRules.Check(repositoryRoot, previous);
            if (problems.Count > 0)
            {
                throw new ReleaseRuleException("A patch release changes stored data: " + string.Join(" ", problems));
            }
        }

        // 026 OD-11: links point at the published documentation of this release's minor line.
        var docs = $"{BuildProps.Read(repositoryRoot).DocsBaseUrl}{core.Major}.{core.Minor}";
        var configuration = hasPrevious ? Configuration(repositoryRoot, previous, fragments, docs) : [];

        var filled = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = version.ToString(),
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["highlights"] = List(fragments.OrderBy(f => f.Kind is "added" or "changed" ? 0 : 1).Select(f => $"{f.Body} (#{f.Issue})")),
            ["added"] = Changes(fragments, "added"),
            ["changed"] = Changes(fragments, "changed"),
            ["fixed"] = Changes(fragments, "fixed"),
            ["security"] = Changes(fragments, "security"),
            ["breaking"] = List(breaking.Select(f => $"{f.Body} (#{f.Issue})")),
            ["manual"] = List(fragments.SelectMany(f => f.Manual.Select(m => $"{m} (#{f.Issue})"))),
            ["configuration"] = List(configuration),
            ["migrations"] = List(migrations),
            ["schemas"] = List(schemas),
            ["background"] = List(fragments.SelectMany(f => f.Background.Select(b => $"{b} (#{f.Issue})"))),
            ["caches"] = List(fragments.SelectMany(f => f.Caches.Select(c => $"{c} (#{f.Issue})"))),
            ["visible"] = List(visible.Select(v => $"{v.Reason} (#{v.Issue}): {string.Join(", ", v.Pages.Select(p => $"`{p}`"))}")),
            ["upgradePath"] = UpgradePath(core, sources, previous),
            ["images"] = $"- `ghcr.io/trungnd1985/quickbuilder:{version}` for linux/amd64 and linux/arm64: {Candidate}",
            ["knownIssues"] = None,
            ["docs"] = docs,
        };

        return Marker().Replace(template, m => filled[m.Groups["name"].Value]);
    }

    private static string Changes(IEnumerable<Fragment> fragments, string kind) =>
        List(fragments.Where(f => f.Kind == kind).Select(f => $"{f.Summary} (#{f.Issue})"));

    private static string List(IEnumerable<string> items)
    {
        var lines = items.ToList();
        return lines.Count == 0 ? None : string.Join('\n', lines.Select(l => $"- {l}"));
    }

    /// <summary>Migrations added since <paramref name="previous"/>, by module.</summary>
    private static List<string> Migrations(string repositoryRoot, string previous) =>
        [.. Git.Changes(repositoryRoot, previous, "src")
            .Where(c => c.Status == 'A' && MigrationFreeze.IsMigration(c.Path) && !c.Path.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Select(c => (Module: ModuleOf(c.Path), Name: MigrationName().Match(c.Path).Groups["name"].Value))
            .OrderBy(m => m.Module, StringComparer.Ordinal)
            .GroupBy(m => m.Module)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(m => $"`{m.Name}`"))}")];

    private static string ModuleOf(string path)
    {
        var parts = path.Split('/');
        var modules = Array.IndexOf(parts, "Modules");
        return modules >= 0 && modules + 1 < parts.Length ? parts[modules + 1] : "Platform";
    }

    /// <summary>Contracts whose version was raised since <paramref name="previous"/>.</summary>
    private static List<string> SchemaRaises(string repositoryRoot, string previous) =>
        [.. Git.Changes(repositoryRoot, previous, "docs/contracts")
            .Where(c => c.Status == 'A' && SchemaFile().Match(c.Path) is { Success: true })
            .Select(c => SchemaFile().Match(c.Path))
            .Select(m => $"{m.Groups["contract"].Value}: version {m.Groups["version"].Value}")];

    /// <summary>
    /// Keys the configuration guide gained and lost since <paramref name="previous"/>, which the
    /// fragments must say too (rule 3), plus the keys fragments say changed.
    /// </summary>
    private static List<string> Configuration(string repositoryRoot, string previous, List<Fragment> fragments, string docs)
    {
        var before = ConfigurationKeys.At(repositoryRoot, previous);
        var now = ConfigurationKeys.At(repositoryRoot, null);
        var added = now.Except(before).Order(StringComparer.Ordinal).ToList();
        var removed = before.Except(now).Order(StringComparer.Ordinal).ToList();
        var saidAdded = fragments.SelectMany(f => f.ConfigAdded).ToHashSet(StringComparer.Ordinal);
        var saidRemoved = fragments.SelectMany(f => f.ConfigRemoved).ToHashSet(StringComparer.Ordinal);

        // 026 T096: a key newly marked deprecated in the reference must be announced too.
        var deprecated = ConfigurationKeys.DeprecatedAt(repositoryRoot, null)
            .Except(ConfigurationKeys.DeprecatedAt(repositoryRoot, previous))
            .Order(StringComparer.Ordinal)
            .ToList();
        var saidDeprecated = fragments.SelectMany(f => f.ConfigDeprecated).ToHashSet(StringComparer.Ordinal);

        var mismatch = added.Where(k => !saidAdded.Contains(k)).Select(k => $"{k} was added to {ConfigurationKeys.Guide} but no fragment says so")
            .Concat(removed.Where(k => !saidRemoved.Contains(k)).Select(k => $"{k} was removed from {ConfigurationKeys.Guide} but no fragment says so"))
            .Concat(saidAdded.Where(k => !added.Contains(k)).Select(k => $"a fragment adds {k}, which {ConfigurationKeys.Guide} did not gain"))
            .Concat(saidRemoved.Where(k => !removed.Contains(k)).Select(k => $"a fragment removes {k}, which {ConfigurationKeys.Guide} still has"))
            .Concat(deprecated.Where(k => !saidDeprecated.Contains(k)).Select(k => $"{k} was deprecated in {ConfigurationKeys.Guide} but no fragment says so"))
            .Concat(saidDeprecated.Where(k => !deprecated.Contains(k)).Select(k => $"a fragment deprecates {k}, which {ConfigurationKeys.Guide} does not newly mark deprecated"))
            .ToList();
        if (mismatch.Count > 0)
        {
            throw new ReleaseRuleException("Configuration keys do not match: " + string.Join("; ", mismatch) + " (contracts/release-notes.md §3 rule 3).");
        }

        return
        [
            .. added.Select(k => $"{Linked(k, docs)} (added)"),
            .. fragments.SelectMany(f => f.ConfigChanged).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(k => $"{Linked(k, docs)} (changed)"),
            .. deprecated.Select(k => $"{Linked(k, docs)} (deprecated)"),
            .. removed.Select(k => $"`{k}` (removed)"),
        ];
    }

    /// <summary>
    /// A key linked to its row in the published reference: the anchor is the key in lower case,
    /// <c>:</c> to <c>-</c>, placeholders such as <c>&lt;n&gt;</c> dropped (026 data-model §3).
    /// </summary>
    internal static string Linked(string key, string docs)
    {
        var anchor = string.Join('-', key.Split(':').Where(s => !s.StartsWith('<')).Select(s => s.ToLowerInvariant()));
        return $"[`{key}`]({docs}/reference/configuration#{anchor})";
    }

    private static string UpgradePath(SemanticVersion core, IReadOnlyList<string> sources, string previous)
    {
        var text = new StringBuilder();
        text.Append(core.Major == 0
            ? "Upgrade directly from any 0.x release by replacing the image."
            : $"Upgrade directly from any {core.Major}.x release by replacing the image.");
        text.Append('\n').Append('\n').Append("Tested in this release from: ").Append(string.Join(", ", sources.Select(s => $"`{s}`"))).Append('.');
        var before = previous.StartsWith('v') && SemanticVersion.TryParse(previous[1..], out var release)
            ? $"QuickBuilder {release}"
            : "the build you ran before";
        text.Append('\n').Append('\n').Append($"Rolling back: restore the backup taken before upgrading, then start {before}.");
        return text.ToString();
    }

    [GeneratedRegex(@"\{\{(?<name>[A-Za-z]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();

    [GeneratedRegex(@"/\d{14}_(?<name>[^/.]+)\.cs$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationName();

    [GeneratedRegex(@"^docs/contracts/(?<contract>[^/]+)/v(?<version>\d+)\.schema\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaFile();
}
