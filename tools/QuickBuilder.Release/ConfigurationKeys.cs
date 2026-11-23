using System.Text.RegularExpressions;

namespace QuickBuilder.Release;

/// <summary>
/// The configuration keys the configuration reference documents: the first, backticked cell of
/// each table row (019 contracts/release-notes.md §3 rule 3).
/// </summary>
/// <remarks>
/// 026 research OD-06: the reference moved and became generated. Each lookup reads the first
/// path in <see cref="Guides"/> that exists at that reference, so a comparison with v0.1.0
/// (old path only) and with later releases (new path) both work.
/// </remarks>
internal static partial class ConfigurationKeys
{
    /// <summary>Where the reference lives, newest first.</summary>
    public static IReadOnlyList<string> Guides { get; } =
        ["docs/operators/reference/configuration.md", "docs/configuration.md"];

    /// <summary>The current reference's path, for messages.</summary>
    public static string Guide => Guides[0];

    /// <summary>True for any path the reference has lived at.</summary>
    public static bool IsGuide(string path) => Guides.Contains(path);

    /// <summary>The keys in the reference at <paramref name="reference"/>, or in the working tree when null.</summary>
    public static IReadOnlySet<string> At(string repositoryRoot, string? reference) =>
        Row().Matches(Text(repositoryRoot, reference)).Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>The keys the reference marks deprecated at <paramref name="reference"/>, or in the working tree when null.</summary>
    public static IReadOnlySet<string> DeprecatedAt(string repositoryRoot, string? reference) =>
        Row().Matches(Text(repositoryRoot, reference))
            .Where(m => m.Groups["rest"].Value.Contains("**Deprecated**", StringComparison.Ordinal))
            .Select(m => m.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Text(string repositoryRoot, string? reference)
    {
        foreach (var guide in Guides)
        {
            if (reference is null)
            {
                var path = Path.Combine(repositoryRoot, guide);
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            else if (Git.Show(repositoryRoot, reference, guide) is { } text)
            {
                return text;
            }
        }

        return string.Empty;
    }

    [GeneratedRegex(@"^\|\s*`(?<key>[A-Za-z][^`]*)`\s*\|(?<rest>.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Row();
}
