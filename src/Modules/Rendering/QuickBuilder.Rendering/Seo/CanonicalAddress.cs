using System.Globalization;
using QuickBuilder.Pages.Contracts.Publishing;

namespace QuickBuilder.Rendering.Seo;

/// <summary>
/// The one address search engines should index for a page or item page (017 FR-011, FR-012;
/// research SE-06).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The site's base URL, then the lower-case address (the home page is <c>/</c>).</item>
/// <item>Then <c>?param=n</c> for each of the page's paginated lists past its first page, in
/// parameter-name order; every other parameter is dropped.</item>
/// <item>A <c>page</c> override names that page's current live address; a target that is not
/// live is ignored, and the page names itself.</item>
/// <item>A <c>url</c> override is used as written: the schema already allowed only absolute
/// <c>http</c> and <c>https</c> addresses.</item>
/// </list>
/// Pure: the golden tests and every render share it.
/// </remarks>
internal static class CanonicalAddress
{
    public static string Of(
        string baseUrl,
        string path,
        IReadOnlyDictionary<string, int>? pageParams,
        SeoCanonical? canonicalOverride,
        IReadOnlyDictionary<Guid, PageLinkTarget>? targets)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(path);

        switch (canonicalOverride)
        {
            case { Href: { } href }:
                return href;

            case { PageId: { } pageId } when targets is not null && targets.TryGetValue(pageId, out var target) && target.IsLive:
                return SiteAddress.Absolute(baseUrl, target.Path.ToLowerInvariant());
        }

        var self = SiteAddress.Absolute(baseUrl, path.ToLowerInvariant());
        var pages = (pageParams ?? new Dictionary<string, int>())
            .Where(p => p.Value > 1)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={p.Value.ToString(CultureInfo.InvariantCulture)}")
            .ToList();

        return pages.Count == 0 ? self : self + "?" + string.Join('&', pages);
    }
}
