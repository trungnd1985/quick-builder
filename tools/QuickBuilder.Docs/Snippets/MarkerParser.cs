using System.Text.RegularExpressions;

namespace QuickBuilder.Docs.Snippets;

/// <summary>A marked block on an operator page (026 data-model §6).</summary>
internal sealed record Snippet(
    string Procedure,
    string Kind,
    int Step,
    string? FilePath,
    IReadOnlyList<string> Substitutes,
    string Language,
    string Body,
    string Source,
    int Line);

/// <summary>
/// Finds <c>&lt;!-- qb:run … --&gt;</c> and <c>&lt;!-- qb:file … --&gt;</c> markers and the fenced
/// block after each (026 contracts/runnable-snippets.md §1).
/// </summary>
internal static partial class MarkerParser
{
    public static IReadOnlyList<string> Procedures { get; } =
        ["install", "backup", "restore", "nginx", "caddy", "traefik", "proxy-basepath"];

    /// <summary>Parses one page. <paramref name="problems"/> receives every marker error.</summary>
    public static IReadOnlyList<Snippet> Parse(string source, string text, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(problems);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var snippets = new List<Snippet>();
        for (var i = 0; i < lines.Length; i++)
        {
            var marker = Marker().Match(lines[i]);
            if (!marker.Success)
            {
                if (lines[i].Contains("qb:run", StringComparison.Ordinal) || lines[i].Contains("qb:file", StringComparison.Ordinal))
                {
                    if (lines[i].TrimStart().StartsWith("<!--", StringComparison.Ordinal))
                    {
                        problems.Add($"{source}:{i + 1}: malformed marker — write <!-- qb:run procedure=<name> step=<n> -->.");
                    }
                }

                continue;
            }

            var where = $"{source}:{i + 1}";
            var kind = marker.Groups["kind"].Value;
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match pair in Attribute().Matches(marker.Groups["attributes"].Value))
            {
                attributes[pair.Groups["name"].Value] = pair.Groups["value"].Value;
            }

            var fence = i + 1 < lines.Length ? Fence().Match(lines[i + 1]) : Match.Empty;
            if (!fence.Success)
            {
                problems.Add($"{where}: qb:{kind} marker is not followed by a fenced block — put the ``` block on the next line.");
                continue;
            }

            var close = Array.FindIndex(lines, i + 2, l => l.TrimEnd() == fence.Groups["fence"].Value);
            if (close < 0)
            {
                problems.Add($"{where}: the fenced block after the marker is not closed.");
                continue;
            }

            var body = string.Join('\n', lines[(i + 2)..close]);
            var language = fence.Groups["language"].Value;
            i = close;

            if (!attributes.TryGetValue("procedure", out var procedure) || !Procedures.Contains(procedure))
            {
                problems.Add($"{where}: unknown procedure '{attributes.GetValueOrDefault("procedure")}' — use one of {string.Join(", ", Procedures)}.");
                continue;
            }

            if (!attributes.TryGetValue("step", out var stepText) || !int.TryParse(stepText, out var step) || step < 1)
            {
                problems.Add($"{where}: step must be a whole number from 1.");
                continue;
            }

            var substitutes = attributes.TryGetValue("substitute", out var listed)
                ? listed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
            if (substitutes.FirstOrDefault(s => !Substitutions.Names.Contains(s)) is { } unknown)
            {
                problems.Add($"{where}: unknown substitution '{unknown}' — use one of {string.Join(", ", Substitutions.Names)}.");
                continue;
            }

            attributes.TryGetValue("path", out var path);
            if (kind == "file")
            {
                if (string.IsNullOrEmpty(path))
                {
                    problems.Add($"{where}: qb:file needs path=<relative path>.");
                    continue;
                }

                if (Path.IsPathRooted(path) || path.Split('/', '\\').Contains(".."))
                {
                    problems.Add($"{where}: path '{path}' must be relative and stay inside the working directory.");
                    continue;
                }
            }
            else if (language is not ("bash" or "sh"))
            {
                problems.Add($"{where}: qb:run blocks must be bash or sh, not '{language}'.");
                continue;
            }

            snippets.Add(new Snippet(procedure, kind, step, path, substitutes, language, body, source, i + 1));
        }

        return snippets;
    }

    /// <summary>Checks the rules that span pages: unique steps, and a run step in every procedure.</summary>
    public static void CheckProcedures(IReadOnlyList<Snippet> snippets, List<string> problems)
    {
        foreach (var procedure in snippets.GroupBy(s => s.Procedure))
        {
            foreach (var duplicate in procedure.GroupBy(s => s.Step).Where(g => g.Count() > 1))
            {
                problems.Add($"{procedure.Key}: step {duplicate.Key} is used {duplicate.Count()} times ({string.Join(", ", duplicate.Select(d => $"{d.Source}:{d.Line}"))}) — number steps uniquely.");
            }

            if (!procedure.Any(s => s.Kind == "run"))
            {
                problems.Add($"{procedure.Key}: no qb:run step — a procedure must run something.");
            }
        }
    }

    [GeneratedRegex(@"^\s*<!--\s*qb:(?<kind>run|file)\s+(?<attributes>[^>]*?)\s*-->\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();

    [GeneratedRegex(@"(?<name>[a-z-]+)=(?<value>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"^(?<fence>```+|~~~+)(?<language>[A-Za-z0-9_+-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();
}
