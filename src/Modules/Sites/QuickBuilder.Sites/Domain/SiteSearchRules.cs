using System.Text;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Sites.Domain;

/// <summary>
/// The bounds of a site's search settings (017 FR-006, FR-025, research SE-08).
/// </summary>
internal static class SiteSearchRules
{
    public const int MaxTitleSuffixLength = 60;

    public const int MaxOrganisationNameLength = 200;

    public const int MaxRobotsRulesBytes = 10 * 1024;

    public const int MaxRobotsLineLength = 500;

    public static Error Invalid(string message) => Error.Validation("sites.search_invalid", message);

    /// <summary>Whether a one-line text holds a control character, line breaks included.</summary>
    public static bool HasControlCharacters(string? text) => text is not null && text.Any(char.IsControl);

    /// <summary>
    /// The rules with <c>\n</c> line ends and no trailing blank lines; empty for none; null when
    /// they break a bound (over 10 KB in UTF-8, a line over 500 characters, or a control
    /// character other than a line break or a tab).
    /// </summary>
    public static string? NormaliseRobotsRules(string? rules)
    {
        if (string.IsNullOrWhiteSpace(rules))
        {
            return string.Empty;
        }

        var normalised = rules.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n', ' ', '\t');
        if (Encoding.UTF8.GetByteCount(normalised) > MaxRobotsRulesBytes)
        {
            return null;
        }

        foreach (var line in normalised.Split('\n'))
        {
            if (line.Length > MaxRobotsLineLength || line.Any(c => char.IsControl(c) && c != '\t'))
            {
                return null;
            }
        }

        return normalised;
    }
}
