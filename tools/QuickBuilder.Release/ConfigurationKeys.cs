using System.Text.RegularExpressions;

namespace QuickBuilder.Release;

/// <summary>
/// The configuration keys <c>docs/configuration.md</c> documents: the first, backticked cell of
/// each table row (019 contracts/release-notes.md §3 rule 3).
/// </summary>
internal static partial class ConfigurationKeys
{
    public const string Guide = "docs/configuration.md";

    /// <summary>The keys in the guide at <paramref name="reference"/>, or in the working tree when null.</summary>
    public static IReadOnlySet<string> At(string repositoryRoot, string? reference)
    {
        var text = reference is null
            ? (File.Exists(Path.Combine(repositoryRoot, Guide)) ? File.ReadAllText(Path.Combine(repositoryRoot, Guide)) : string.Empty)
            : Git.Show(repositoryRoot, reference, Guide) ?? string.Empty;

        return Row().Matches(text).Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^\|\s*`(?<key>[A-Za-z][^`]*)`\s*\|", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Row();
}
