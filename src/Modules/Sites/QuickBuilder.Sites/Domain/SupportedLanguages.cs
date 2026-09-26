namespace QuickBuilder.Sites.Domain;

/// <summary>
/// The languages a site may choose as its default (009 research S-09).
/// </summary>
/// <remarks>
/// A fixed list, not operator-editable (clarification: curated list, English by default).
/// Tags are BCP 47 in their canonical case. The admin shell renders their names with the
/// browser's <c>Intl.DisplayNames</c>, so no name table ships here or in translations.
/// Adding a tag is a code change with no data migration; removing one would strand sites
/// that chose it, so the list only grows.
/// </remarks>
internal static class SupportedLanguages
{
    public const string Default = "en";

    public static IReadOnlyList<string> All { get; } =
    [
        "ar", "bg", "cs", "da", "de", "el", "en", "es", "et", "fa",
        "fi", "fr", "he", "hi", "hr", "hu", "id", "it", "ja", "ko",
        "lt", "lv", "ms", "nb", "nl", "pl", "pt", "pt-BR", "ro", "ru",
        "sk", "sl", "sr", "sv", "th", "tr", "uk", "vi", "zh-Hans", "zh-Hant",
    ];

    /// <summary>
    /// Finds a supported tag regardless of case, returning it in canonical case.
    /// </summary>
    public static bool TryCanonicalise(string? tag, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var trimmed = tag.Trim();
        foreach (var supported in All)
        {
            if (string.Equals(supported, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                canonical = supported;
                return true;
            }
        }

        return false;
    }
}
