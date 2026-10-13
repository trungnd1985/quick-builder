using QuickBuilder.Media.Contracts;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Seo;

/// <summary>What the head of a published page or item page says about it (017 research SE-05).</summary>
/// <param name="Title">The document title.</param>
/// <param name="Description">The meta description; null writes none.</param>
/// <param name="Canonical">The absolute canonical address (017 FR-011).</param>
/// <param name="Robots">The robots directive, or null when the page may be indexed.</param>
/// <param name="OgType"><c>article</c> on item pages, <c>website</c> elsewhere.</param>
/// <param name="OgTitle">The sharing title.</param>
/// <param name="OgDescription">The sharing description; null writes none.</param>
/// <param name="SiteName">The site's name.</param>
/// <param name="OgLocale">The default language as an Open Graph locale.</param>
/// <param name="Image">The sharing image, or null for none.</param>
internal sealed record HeadMetadata(
    string Title,
    string? Description,
    string Canonical,
    string? Robots,
    string OgType,
    string OgTitle,
    string? OgDescription,
    string SiteName,
    string OgLocale,
    SharingImage? Image);

/// <summary>A sharing image, at an absolute address.</summary>
internal sealed record SharingImage(string Url, int? Width, int? Height, string? Alt);

/// <summary>Everything the head of a page is built from (017 research SE-05).</summary>
/// <param name="PageTitle">The live version's title, or for an item page its name (016 FR-030).</param>
/// <param name="Path">The page's normalised address, <c>/</c> for the home page.</param>
/// <param name="IsItemPage">Whether this is an item's page under a template.</param>
/// <param name="Seo">The live document's metadata, or null when it has none.</param>
/// <param name="Site">The site: name, base URL, language and search settings.</param>
/// <param name="Media">The media files the render fetched; a missing id is a deleted file.</param>
internal sealed record HeadInputs(
    string PageTitle,
    string Path,
    bool IsItemPage,
    SeoDocument? Seo,
    SiteDto Site,
    IReadOnlyDictionary<Guid, PublicMediaFile> Media);
