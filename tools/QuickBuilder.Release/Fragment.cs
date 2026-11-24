using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickBuilder.Release;

/// <summary>
/// One release-note fragment, <c>docs/releases/unreleased/&lt;issue&gt;-&lt;slug&gt;.md</c>
/// (019 contracts/release-notes.md §2).
/// </summary>
/// <remarks>
/// Front matter is a fixed set of keys with scalars and one-line flow lists, parsed by hand:
/// a YAML library for eight keys would be a dependency without a reason (research RL-22).
/// </remarks>
internal sealed partial record Fragment(
    string File,
    int Issue,
    string Kind,
    bool Breaking,
    IReadOnlyList<string> ConfigAdded,
    IReadOnlyList<string> ConfigChanged,
    IReadOnlyList<string> ConfigRemoved,
    IReadOnlyList<string> Background,
    IReadOnlyList<string> Caches,
    IReadOnlyList<string> Manual,
    string Body)
{
    /// <summary>Keys this change deprecates (<c>config.deprecated</c>; 026 T096, FR-028).</summary>
    public IReadOnlyList<string> ConfigDeprecated { get; init; } = [];

    internal static readonly string[] Kinds = ["added", "changed", "fixed", "security"];

    private const int MaxSentences = 5;

    /// <summary>The body's first sentence, for the Changes list.</summary>
    public string Summary => FirstSentence().Match(Body) is { Success: true } m ? m.Value.Trim() : Body;

    /// <exception cref="ReleaseRuleException">The fragment is malformed; the message names the file and line.</exception>
    public static Fragment Parse(string file, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            throw Error(file, 1, "must start with front matter between --- lines");
        }

        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0)
        {
            throw Error(file, 1, "front matter is not closed with ---");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        string? parent = null;

        for (var i = 1; i < end; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                throw Error(file, i + 1, "lists must be one-line flow lists, like [a, \"b, c\"]");
            }

            var indented = line.StartsWith(' ');
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                throw Error(file, i + 1, $"'{line.Trim()}' is not 'key: value'");
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (indented)
            {
                if (parent != "config" || key is not ("added" or "changed" or "deprecated" or "removed"))
                {
                    throw Error(file, i + 1, $"unknown key '{key}'");
                }

                lists[$"config.{key}"] = FlowList(file, i + 1, value);
                continue;
            }

            parent = key;
            switch (key)
            {
                case "issue" or "kind" or "breaking":
                    values[key] = value;
                    break;
                case "config":
                    if (value.Length > 0)
                    {
                        throw Error(file, i + 1, "config holds added, changed, deprecated and removed, one per line");
                    }

                    break;
                case "background" or "caches" or "manual":
                    lists[key] = FlowList(file, i + 1, value);
                    break;
                default:
                    throw Error(file, i + 1, $"unknown key '{key}'");
            }
        }

        if (!values.TryGetValue("issue", out var issueText) || !int.TryParse(issueText, NumberStyles.None, CultureInfo.InvariantCulture, out var issue) || issue <= 0)
        {
            throw Error(file, 1, "issue must be a positive number");
        }

        if (!Path.GetFileName(file).StartsWith($"{issue}-", StringComparison.Ordinal))
        {
            throw Error(file, 1, $"the file name must start with the issue, {issue}-");
        }

        if (!values.TryGetValue("kind", out var kind) || !Kinds.Contains(kind))
        {
            throw Error(file, 1, $"kind must be one of {string.Join(", ", Kinds)}");
        }

        if (!values.TryGetValue("breaking", out var breakingText) || breakingText is not ("true" or "false"))
        {
            throw Error(file, 1, "breaking must be true or false");
        }

        var body = string.Join('\n', lines[(end + 1)..]).Trim();
        if (body.Length == 0)
        {
            throw Error(file, end + 2, "the body is empty: say what changed in one to five sentences");
        }

        if (body.Split('\n').Any(l => l.TrimStart().StartsWith('#')))
        {
            throw Error(file, end + 2, "the body has a heading; write sentences only");
        }

        if (Sentence().Count(body) > MaxSentences)
        {
            throw Error(file, end + 2, $"the body has more than {MaxSentences} sentences");
        }

        return new Fragment(
            file,
            issue,
            kind,
            breakingText == "true",
            lists.GetValueOrDefault("config.added", []),
            lists.GetValueOrDefault("config.changed", []),
            lists.GetValueOrDefault("config.removed", []),
            lists.GetValueOrDefault("background", []),
            lists.GetValueOrDefault("caches", []),
            lists.GetValueOrDefault("manual", []),
            body)
        {
            ConfigDeprecated = lists.GetValueOrDefault("config.deprecated", []),
        };
    }

    /// <summary><c>[a, "b, c"]</c> → a, b, c — quoted items may hold commas.</summary>
    private static List<string> FlowList(string file, int line, string value)
    {
        if (!value.StartsWith('[') || !value.EndsWith(']'))
        {
            throw Error(file, line, "lists must be one-line flow lists, like [a, \"b, c\"]");
        }

        var items = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in value[1..^1])
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                Add(items, current);
            }
            else
            {
                current.Append(c);
            }
        }

        if (quoted)
        {
            throw Error(file, line, "a quote is not closed");
        }

        Add(items, current);
        return items;
    }

    private static void Add(List<string> items, StringBuilder current)
    {
        var item = current.ToString().Trim();
        if (item.Length > 0)
        {
            items.Add(item);
        }

        current.Clear();
    }

    private static ReleaseRuleException Error(string file, int line, string message) =>
        new($"{Path.GetFileName(file)}:{line}: {message} (contracts/release-notes.md §2).");

    [GeneratedRegex(@"[.!?](\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Sentence();

    [GeneratedRegex(@"^.*?[.!?](?=\s|$)", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex FirstSentence();
}
