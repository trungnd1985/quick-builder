using System.Globalization;
using System.Text;

namespace QuickBuilder.Media.Domain;

/// <summary>
/// Cleans an uploaded file's name for display (003 research M-17).
/// </summary>
/// <remarks>
/// The cleaned name is shown in the library and sent in <c>Content-Disposition</c>. It is
/// never used to decide where bytes are stored or what the public address is (FR-021) —
/// which is why these rules can be about readability and deception rather than about
/// path safety.
/// </remarks>
internal static class MediaFileName
{
    /// <summary>The longest a cleaned name may be, in UTF-16 code units.</summary>
    public const int MaxLength = 255;

    /// <summary>
    /// The cleaned name, or null when nothing usable remains.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>Percent-decode (the name travels percent-encoded in a header, M-10).</item>
    /// <item>Keep only the part after the last <c>/</c> or <c>\</c>.</item>
    /// <item>Strip control (Cc) and format (Cf) characters. Format characters include the
    /// right-to-left override that makes <c>gpj.exe</c> display as <c>exe.jpg</c>.</item>
    /// <item>Trim whitespace and dots at both ends.</item>
    /// <item>Shorten the stem so the whole fits in 255, keeping the extension.</item>
    /// </list>
    /// Names are not Unicode-normalised: the application runs with invariant globalization,
    /// where <c>string.Normalize</c> does nothing, and normalisation was only ever for tidy
    /// display (research M-17). A decomposed "é" is shown as the browser renders it.
    /// </remarks>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
            decoded = raw;
        }

        var lastSeparator = decoded.LastIndexOfAny(['/', '\\']);
        var name = lastSeparator >= 0 ? decoded[(lastSeparator + 1)..] : decoded;

        // EnumerateRunes replaces ill-formed UTF-16 (a lone surrogate) with U+FFFD.
        var builder = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                builder.Append(rune.ToString());
            }
        }

        name = builder.ToString().Trim().Trim('.').Trim();
        if (name.Length == 0)
        {
            return null;
        }

        return name.Length <= MaxLength ? name : Shorten(name);
    }

    /// <summary>The lower-case extension of a cleaned name, without the dot, or empty.</summary>
    public static string Extension(string cleaned)
    {
        ArgumentNullException.ThrowIfNull(cleaned);

        var dot = cleaned.LastIndexOf('.');
        return dot <= 0 || dot == cleaned.Length - 1
            ? string.Empty
            : cleaned[(dot + 1)..].ToLowerInvariant();
    }

    private static string Shorten(string name)
    {
        var dot = name.LastIndexOf('.');
        var extension = dot > 0 && name.Length - dot <= 16 ? name[dot..] : string.Empty;
        var stemBudget = MaxLength - extension.Length;
        var stem = name[..(name.Length - extension.Length)];

        // Never cut a surrogate pair in half.
        var cut = stemBudget;
        if (cut > 0 && char.IsHighSurrogate(stem[cut - 1]))
        {
            cut--;
        }

        return stem[..cut].TrimEnd() + extension;
    }
}
