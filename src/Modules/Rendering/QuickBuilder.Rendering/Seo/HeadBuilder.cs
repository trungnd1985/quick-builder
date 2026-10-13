using QuickBuilder.Media.Contracts;

namespace QuickBuilder.Rendering.Seo;

/// <summary>
/// Decides what a page's head says, with the fallbacks of 017 FR-005 (research SE-05).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Title: the meta title as written; else the page title followed by the site's title
/// suffix.</item>
/// <item>Description: the meta description, else none.</item>
/// <item>Sharing title and description: their own, else the title and the description.</item>
/// <item>Sharing image: the page's, else the site's default, else none. A deleted file is
/// none.</item>
/// <item>Robots: <c>noindex</c> for a hidden page; <c>noindex, nofollow</c> for every page
/// of a site that discourages search engines (FR-027).</item>
/// </list>
/// Pure: it reads only its inputs, so the golden files pin it down.
/// </remarks>
internal static class HeadBuilder
{
    /// <summary>The widest variant offered to link previews; services show about this size.</summary>
    internal const int MaxSharingImageWidth = 1200;

    public static HeadMetadata Build(HeadInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var seo = inputs.Seo;
        var site = inputs.Site;

        var title = seo?.Title ?? inputs.PageTitle + (site.Search.TitleSuffix ?? string.Empty);
        var description = seo?.Description;
        var canonical = SiteAddress.Absolute(site.BaseUrl, inputs.Path);

        string? robots = site.Search.Discouraged
            ? "noindex, nofollow"
            : seo is { NoIndex: true } ? "noindex" : null;

        var imageId = seo?.OgImageId ?? site.Search.DefaultImageId;
        var image = imageId is { } id && inputs.Media.TryGetValue(id, out var file)
            ? SharingImageOf(file, site.BaseUrl)
            : null;

        return new HeadMetadata(
            title,
            description,
            canonical,
            robots,
            inputs.IsItemPage ? "article" : "website",
            seo?.OgTitle ?? title,
            seo?.OgDescription ?? description,
            site.Name,
            OgLocale.From(site.DefaultLanguage),
            image);
    }

    /// <summary>The media ids a page's head may show, to fetch with the page's other files.</summary>
    public static IEnumerable<Guid> MediaIds(SeoDocument? seo, Sites.Contracts.SiteDto site)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (seo?.OgImageId is { } own)
        {
            yield return own;
        }

        if (site.Search.DefaultImageId is { } fallback)
        {
            yield return fallback;
        }
    }

    /// <summary>The largest variant up to <see cref="MaxSharingImageWidth"/>, else the original.</summary>
    internal static SharingImage SharingImageOf(PublicMediaFile file, string baseUrl)
    {
        var variant = file.Variants.Where(v => v.Width <= MaxSharingImageWidth).MaxBy(v => v.Width);
        if (variant is null)
        {
            return new SharingImage(SiteAddress.Absolute(baseUrl, file.PublicPath), file.Width, file.Height, file.AltText);
        }

        int? height = file is { Width: > 0, Height: { } h }
            ? (int)Math.Round(h * (double)variant.Width / file.Width.Value, MidpointRounding.AwayFromZero)
            : null;
        return new SharingImage(SiteAddress.Absolute(baseUrl, variant.PublicPath), variant.Width, height, file.AltText);
    }
}

/// <summary>Absolute addresses on a site, built from its base URL, never from a request (009 S-07).</summary>
internal static class SiteAddress
{
    /// <summary>
    /// <paramref name="baseUrl"/> (with or without its trailing slash) followed by
    /// <paramref name="path"/>, which starts with <c>/</c>.
    /// </summary>
    public static string Absolute(string baseUrl, string path)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(path);

        return baseUrl.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path);
    }
}
