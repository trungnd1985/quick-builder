using System.Text.RegularExpressions;
using QuickBuilder.Docs.Settings;
using QuickBuilder.Docs.Snippets;
using QuickBuilder.Docs.UpgradeNotes;

namespace QuickBuilder.Docs.Lint;

/// <summary>
/// <c>settings-exist</c>: every setting named in prose, compose files or examples exists
/// (FR-003, research OD-19).
/// </summary>
internal sealed partial class SettingsExistCheck : ILintCheck
{
    private static readonly string[] RepositoryFiles = ["docker-compose.yml", "README.md"];

    public string Name => "settings-exist";

    public static IEnumerable<string> ScannedFiles(LintContext context) =>
        context.Pages
            .Where(p => !p.EndsWith("reference/configuration.md", StringComparison.Ordinal) && !p.EndsWith("upgrade-notes.md", StringComparison.Ordinal))
            .Concat(RepositoryFiles.Where(context.Exists));

    public IEnumerable<string> Run(LintContext context)
    {
        var catalog = context.Catalog;
        var roots = catalog.Sections.Select(s => s.Key.Split(':')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ScannedFiles(context))
        {
            var lines = context.Read(file).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var names = Colon().Matches(lines[i]).Select(m => m.Groups["key"].Value)
                    .Concat(Environment().Matches(lines[i]).Select(m => m.Groups["key"].Value.Replace("__", ":", StringComparison.Ordinal)));
                foreach (var name in names.Distinct())
                {
                    var key = Normalise(name);
                    if (!roots.Contains(key.Split(':')[0]) || catalog.Declares(key) || catalog.IsParentPath(key))
                    {
                        continue;
                    }

                    yield return $"{file}:{i + 1}: '{name}' is not a setting QuickBuilder reads — fix the name, or declare the setting.";
                }
            }
        }
    }

    /// <summary>Drops <c>:&lt;n&gt;</c> placeholders and list indexes, so entries match their list.</summary>
    internal static string Normalise(string name) =>
        string.Join(':', name.Split(':').Where(s => !s.StartsWith('<') && !s.All(char.IsAsciiDigit)));

    [GeneratedRegex(@"`(?<key>[A-Z][A-Za-z0-9]*(?::[A-Za-z0-9<>]+)+)`", RegexOptions.CultureInvariant)]
    private static partial Regex Colon();

    [GeneratedRegex(@"\b(?<key>[A-Z][A-Za-z0-9]*(?:__[A-Za-z0-9<>]+)+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Environment();
}

/// <summary>
/// <c>notes-restate</c>: hand-written notes never repeat the generated table (FR-013).
/// </summary>
internal sealed partial class NotesRestateCheck : ILintCheck
{
    public string Name => "notes-restate";

    public IEnumerable<string> Run(LintContext context)
    {
        foreach (var page in context.Pages.Where(p => p.StartsWith(ReferenceWriter.NotesDirectory + "/", StringComparison.Ordinal)))
        {
            var lines = context.Read(page).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (KeyRow().IsMatch(lines[i]))
                {
                    yield return $"{page}:{i + 1}: a table row for a setting — the generated table already lists it; explain instead of repeating it.";
                }
                else if (DefaultSentence().IsMatch(lines[i]))
                {
                    yield return $"{page}:{i + 1}: states a setting's default — the generated table gives defaults; drop the number here.";
                }
            }
        }
    }

    [GeneratedRegex(@"^\|\s*`[A-Za-z][^`]*`\s*\|", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRow();

    [GeneratedRegex(@"`[A-Z][A-Za-z0-9]*(?::[A-Za-z0-9]+)+`[^.]*\bdefaults? (is|to) `", RegexOptions.CultureInvariant)]
    private static partial Regex DefaultSentence();
}

/// <summary>
/// <c>example-secrets</c>: examples give secret settings only placeholders (edge case
/// "secret values in examples").
/// </summary>
internal sealed partial class ExampleSecretsCheck : ILintCheck
{
    public string Name => "example-secrets";

    public IEnumerable<string> Run(LintContext context)
    {
        var secrets = context.Catalog.Settings.Where(s => s.Secret).ToList();
        foreach (var file in SettingsExistCheck.ScannedFiles(context))
        {
            var lines = context.Read(file).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var setting in secrets)
                {
                    var environment = setting.Path.Replace(":", "__", StringComparison.Ordinal);
                    foreach (var name in new[] { environment, setting.Path })
                    {
                        var at = lines[i].IndexOf(name, StringComparison.Ordinal);
                        if (at < 0)
                        {
                            continue;
                        }

                        var assigned = Assignment().Match(lines[i][(at + name.Length)..]);
                        if (assigned.Success && !IsPlaceholder(assigned.Groups["value"].Value))
                        {
                            yield return $"{file}:{i + 1}: {name} is given a real-looking value — use change-me-before-real-use, <…> or ${{…}} in examples.";
                        }
                    }
                }
            }
        }
    }

    internal static bool IsPlaceholder(string value)
    {
        var v = value.Trim().Trim('"', '\'');
        return v.Length == 0
               || v.StartsWith("change-me", StringComparison.Ordinal)
               || v.StartsWith('<')
               || v.StartsWith("${", StringComparison.Ordinal)
               || v.StartsWith('$')
               || v.Contains("Password=change-me", StringComparison.Ordinal)
               || v.Contains("Password=${", StringComparison.Ordinal)
               || v.Contains("Password=<", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^[`""']?\s*(?:=|:)\s*(?<value>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Assignment();
}

/// <summary><c>snippet-markers</c>: the marker rules of data-model §6.</summary>
internal sealed class SnippetMarkersCheck : ILintCheck
{
    public string Name => "snippet-markers";

    public IEnumerable<string> Run(LintContext context) => SnippetCommands.Collect(context.RepositoryRoot).Problems;
}

/// <summary>
/// <c>proxy-version</c>: each proxy page names the version it was checked with, and its
/// compose block pins the same image tag (FR-021).
/// </summary>
internal sealed partial class ProxyVersionCheck : ILintCheck
{
    public static IReadOnlyList<(string Page, string Name, string Image)> Proxies { get; } =
    [
        ("docs/operators/reverse-proxy/nginx.md", "nginx", "nginx"),
        ("docs/operators/reverse-proxy/caddy.md", "Caddy", "caddy"),
        ("docs/operators/reverse-proxy/traefik.md", "Traefik", "traefik"),
    ];

    public string Name => "proxy-version";

    public IEnumerable<string> Run(LintContext context)
    {
        foreach (var (page, name, image) in Proxies)
        {
            if (!context.Exists(page))
            {
                yield return $"{page}: missing.";
                continue;
            }

            foreach (var finding in Check(page, name, image, context.Read(page)))
            {
                yield return finding;
            }
        }
    }

    internal static IEnumerable<string> Check(string page, string name, string image, string text)
    {
        var stated = Regex.Match(text, $@"Checked with {Regex.Escape(name)} (?<version>[0-9]+(?:\.[0-9A-Za-z-]+)*)", RegexOptions.CultureInvariant);
        if (!stated.Success)
        {
            yield return $"{page}: does not say \"Checked with {name} <version>\" — state the version the example was tested with.";
            yield break;
        }

        var pinned = Regex.Matches(text, $@"image:\s*{Regex.Escape(image)}:(?<tag>[0-9]+(?:\.[0-9A-Za-z-]+)*)", RegexOptions.CultureInvariant)
            .Select(m => m.Groups["tag"].Value)
            .Distinct()
            .ToList();
        if (pinned.Count == 0)
        {
            yield return $"{page}: no 'image: {image}:<version>' in the compose example — pin the version you checked.";
        }
        else if (pinned.Any(t => t != stated.Groups["version"].Value))
        {
            yield return $"{page}: checked with {name} {stated.Groups["version"].Value} but the example pins {string.Join(", ", pinned)} — make them the same.";
        }
    }
}

/// <summary>
/// <c>upgrade-notes-current</c>: the committed upgrade notes match the fragments (OD-16), so
/// a release never finds them stale.
/// </summary>
internal sealed class UpgradeNotesCurrentCheck : ILintCheck
{
    public string Name => "upgrade-notes-current";

    public IEnumerable<string> Run(LintContext context)
    {
        string expected;
        try
        {
            expected = UpgradeNotesGenerator.Render(context.RepositoryRoot, UpgradeNotesGenerator.NextRelease(context.RepositoryRoot));
        }
        catch (Exception e) when (e.GetType().Name == "ReleaseRuleException")
        {
            return [$"{UpgradeNotesGenerator.PagePath}: {e.Message}"];
        }

        var actual = context.Exists(UpgradeNotesGenerator.PagePath) ? context.Read(UpgradeNotesGenerator.PagePath) : null;
        return SettingsCommands.Difference(expected, actual) is { } difference
            ? [$"{UpgradeNotesGenerator.PagePath}: {difference} — Run: dotnet run --project tools/QuickBuilder.Docs -- upgrade-notes generate"]
            : [];
    }
}
