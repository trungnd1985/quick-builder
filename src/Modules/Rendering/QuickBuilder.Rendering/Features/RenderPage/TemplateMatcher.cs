using System.Text.RegularExpressions;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// Splits a normalised address into the address of a template's parent and an item slug
/// (016 research TB-03; public-rendering.md §1).
/// </summary>
/// <remarks>
/// Only an address of two segments or more can be an item's: a template always has a parent
/// that gives it a fixed segment (TB-02), so a one-segment address is never looked up as an
/// item. The last segment must be a slug as the item contract writes them (015), at most 100
/// characters; anything else is not an item address and goes straight to the not-found page.
/// </remarks>
internal static partial class TemplateMatcher
{
    public const int MaxSlugLength = 100;

    /// <summary>The parent address and the slug, or null when the address cannot be an item's.</summary>
    public static (string ParentPath, string Slug)? Split(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var last = path.LastIndexOf('/');
        if (last <= 0 || last == path.Length - 1)
        {
            return null;
        }

        var slug = path[(last + 1)..];
        return slug.Length <= MaxSlugLength && Slug().IsMatch(slug) ? (path[..last], slug) : null;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();
}
