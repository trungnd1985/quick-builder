using System.Text.RegularExpressions;

namespace QuickBuilder.Docs.Snippets;

/// <summary>
/// The closed list of differences between what an operator runs and what CI runs (026
/// contracts/runnable-snippets.md §2). A substitution applies only inside blocks marked with
/// its name; nothing else in a page may differ.
/// </summary>
internal static partial class Substitutions
{
    public const string ReleaseCompose = "release-compose";
    public const string Image = "image";
    public const string TestCertificates = "test-certificates";
    public const string HostNames = "host-names";
    public const string Secret = "secret";
    public const string Ports = "ports";

    /// <summary>The placeholder an operator replaces with a generated password.</summary>
    public const string SecretPlaceholder = "change-me-before-real-use";

    public static IReadOnlyList<string> Names { get; } = [ReleaseCompose, Image, TestCertificates, HostNames, Ports, Secret];

    /// <summary>
    /// Applies substitution <paramref name="name"/> with <paramref name="value"/> to a block's
    /// body. Returns null and adds a problem when the value does not fit the block.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>release-compose</c>: the value is a path; the whole block becomes a copy of that file to <c>docker-compose.yml</c>.</item>
    /// <item><c>image</c>: the value is an image reference; every <c>ghcr.io/trungnd1985/quickbuilder:…</c> becomes it.</item>
    /// <item><c>secret</c>: the value replaces every <see cref="SecretPlaceholder"/>.</item>
    /// <item><c>test-certificates</c>, <c>host-names</c>, <c>ports</c>: the value is <c>from&gt;to</c> pairs separated by <c>;</c>; each <c>from</c> must occur in the block, and <c>\n</c> in a <c>to</c> is a line break.</item>
    /// </list>
    /// </remarks>
    public static string? Apply(string name, string value, string body, string where, List<string> problems) =>
        Apply(name, value, body, where, problems, matched: null);

    /// <summary>
    /// As <see cref="Apply(string, string, string, string, List{string})"/>; for pair substitutions,
    /// a pair whose <c>from</c> is not in this block is skipped and recorded in
    /// <paramref name="matched"/> only when found, so a caller applying one value to several
    /// blocks checks that every pair matched somewhere. Without <paramref name="matched"/>, every
    /// pair must match this block.
    /// </summary>
    public static string? Apply(string name, string value, string body, string where, List<string> problems, ISet<string>? matched)
    {
        switch (name)
        {
            case ReleaseCompose:
                return $"cp '{value.Replace("'", "'\\''", StringComparison.Ordinal)}' ./docker-compose.yml";

            case Image:
                if (!ImageReference().IsMatch(body))
                {
                    problems.Add($"{where}: marked substitute=image but names no ghcr.io/trungnd1985/quickbuilder image.");
                    return null;
                }

                return ImageReference().Replace(body, value);

            case Secret:
                if (!body.Contains(SecretPlaceholder, StringComparison.Ordinal))
                {
                    problems.Add($"{where}: marked substitute=secret but has no '{SecretPlaceholder}' placeholder.");
                    return null;
                }

                return body.Replace(SecretPlaceholder, value, StringComparison.Ordinal);

            case TestCertificates or HostNames or Ports:
                foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = pair.Split('>', 2);
                    if (parts.Length != 2)
                    {
                        problems.Add($"{where}: substitution {name} takes from>to pairs separated by ';', not '{pair}'.");
                        return null;
                    }

                    if (!body.Contains(parts[0], StringComparison.Ordinal))
                    {
                        if (matched is not null)
                        {
                            continue;
                        }

                        problems.Add($"{where}: substitution {name} expected '{parts[0]}' in the block — the page changed; update the test's substitution.");
                        return null;
                    }

                    matched?.Add($"{name}:{parts[0]}");
                    body = body.Replace(parts[0], parts[1].Replace(@"\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
                }

                return body;

            default:
                problems.Add($"{where}: unknown substitution '{name}'.");
                return null;
        }
    }

    [GeneratedRegex(@"ghcr\.io/trungnd1985/quickbuilder:[A-Za-z0-9._<>{}-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ImageReference();
}
