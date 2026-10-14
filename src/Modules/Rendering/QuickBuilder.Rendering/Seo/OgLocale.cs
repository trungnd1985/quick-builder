namespace QuickBuilder.Rendering.Seo;

/// <summary>A BCP 47 language tag as an Open Graph locale (017 research SE-05).</summary>
/// <remarks>
/// Open Graph writes <c>language_TERRITORY</c>: <c>pt-BR</c> becomes <c>pt_BR</c>. A tag
/// without a region (<c>en</c>, <c>vi</c>) stays its language, and a script subtag
/// (<c>zh-Hant</c>) is dropped, since the format has no place for it.
/// </remarks>
internal static class OgLocale
{
    public static string From(string languageTag)
    {
        ArgumentNullException.ThrowIfNull(languageTag);

        var parts = languageTag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "en";
        }

        var language = parts[0].ToLowerInvariant();
        var region = parts.Skip(1).FirstOrDefault(p => p.Length == 2 && p.All(char.IsAsciiLetter));
        return region is null ? language : $"{language}_{region.ToUpperInvariant()}";
    }
}
