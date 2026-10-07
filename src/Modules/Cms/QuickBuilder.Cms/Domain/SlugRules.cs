using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickBuilder.Shared.Kernel.Text;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// An item's slug: its grammar and the suggestions offered for it (015 FR-003, research IT-03).
/// </summary>
/// <remarks>
/// <para>
/// The grammar is the page slug's (010): lower-case letters and digits in runs joined by single
/// hyphens, at most 100 characters. A slug a member types is checked, never rewritten; only
/// suggestions are built here.
/// </para>
/// <para>
/// <see cref="FromName"/> is the C# twin of the admin's <c>features/pages/slugify.ts</c>. The
/// application runs with invariant globalization, so accents are folded through
/// <see cref="SearchText.Normalise"/> instead of NFD; both give the same slug for Latin text.
/// </para>
/// </remarks>
internal static partial class SlugRules
{
    public const int MaxLength = 100;

    private const string CopySuffix = "-copy";

    public static bool IsValid(string? slug) =>
        slug is { Length: > 0 and <= MaxLength } && Grammar().IsMatch(slug);

    /// <summary>A slug built from an item's name, or empty when the name has nothing to build one from.</summary>
    public static string FromName(string? name)
    {
        var folded = SearchText.Normalise(name);
        var slug = NotSlugText().Replace(folded, "-").Trim('-');
        return Shorten(slug, MaxLength);
    }

    /// <summary>
    /// The slug suggested for a new item: from its name, or <c>item-&lt;yyyymmddhhmmss&gt;</c>
    /// when the name gives nothing, as for a collection without a title field (spec Edge Cases).
    /// </summary>
    public static string Suggest(string? name, DateTimeOffset now)
    {
        var slug = FromName(name);
        return slug.Length > 0 ? slug : "item-" + now.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    }

    /// <summary><paramref name="slug"/> with <c>-n</c> added, shortened so the result fits.</summary>
    public static string Numbered(string slug, int n)
    {
        ArgumentNullException.ThrowIfNull(slug);
        var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
        return Shorten(slug, MaxLength - suffix.Length) + suffix;
    }

    /// <summary>The slugs offered, in order, until one is free: the slug itself, then <c>-2</c>, <c>-3</c>, …</summary>
    public static IEnumerable<string> Candidates(string slug)
    {
        yield return slug;
        for (var n = 2; ; n++)
        {
            yield return Numbered(slug, n);
        }
    }

    /// <summary>The slugs offered for a duplicate (US5 scenario 5): <c>-copy</c>, then <c>-copy-2</c>, …</summary>
    public static IEnumerable<string> CopyCandidates(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);
        var copy = Shorten(slug, MaxLength - CopySuffix.Length) + CopySuffix;
        return Candidates(copy);
    }

    /// <summary>Cut at a hyphen when one is near, so a word is not split, and never end on one.</summary>
    private static string Shorten(string slug, int max)
    {
        if (slug.Length <= max)
        {
            return slug;
        }

        var cut = slug[..(max + 1)];
        var boundary = cut.LastIndexOf('-');
        var shortened = boundary > 0 ? cut[..boundary] : slug[..max];
        return shortened.TrimEnd('-');
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Grammar();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotSlugText();
}
