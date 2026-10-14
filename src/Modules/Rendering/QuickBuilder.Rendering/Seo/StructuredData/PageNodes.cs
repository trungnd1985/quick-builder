using System.Text.Json.Nodes;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Seo.StructuredData;

/// <summary>
/// The page-level nodes of a page's graph (017 research SE-18; public-seo.md §4): the
/// breadcrumb trail on every page but the home page, and the website, with its organisation
/// when named, on the home page.
/// </summary>
internal static class PageNodes
{
    /// <summary>
    /// The trail from the home page to <paramref name="currentName"/>: each live ancestor with
    /// its canonical address, then the page itself without one. When the home page is not live,
    /// the trail starts with the site's name, without a link.
    /// </summary>
    public static JsonObject Breadcrumbs(SiteDto site, IReadOnlyList<LiveAncestor> ancestors, string currentName)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(ancestors);

        var crumbs = new List<(string Name, string? Address)>();
        if (!ancestors.Any(a => a.IsHome))
        {
            crumbs.Add((site.Name, null));
        }

        crumbs.AddRange(ancestors.Select(a => (Name(a.Title, site.Name), (string?)SiteAddress.Absolute(site.BaseUrl, a.IsHome ? "/" : a.Path.ToLowerInvariant()))));
        crumbs.Add((currentName, null));

        var items = new JsonArray();
        for (var i = 0; i < crumbs.Count; i++)
        {
            var item = new JsonObject
            {
                ["@type"] = "ListItem",
                ["position"] = i + 1,
                ["name"] = crumbs[i].Name,
            };
            if (crumbs[i].Address is { } address)
            {
                item["item"] = address;
            }

            items.Add(item);
        }

        return new JsonObject { ["@type"] = "BreadcrumbList", ["itemListElement"] = items };
    }

    /// <summary>The home page's <c>WebSite</c> node, then <c>Organization</c> when the site names one.</summary>
    public static IEnumerable<JsonObject> Home(SiteDto site, IReadOnlyDictionary<Guid, PublicMediaFile> media)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(media);

        var url = SiteAddress.Absolute(site.BaseUrl, "/");
        yield return new JsonObject { ["@type"] = "WebSite", ["name"] = site.Name, ["url"] = url };

        if (site.Search.OrganisationName is { Length: > 0 } organisation)
        {
            var node = new JsonObject { ["@type"] = "Organization", ["name"] = organisation, ["url"] = url };
            if (site.Search.OrganisationLogoId is { } logo && media.TryGetValue(logo, out var file))
            {
                node["logo"] = HeadBuilder.SharingImageOf(file, site.BaseUrl).Url;
            }

            yield return node;
        }
    }

    private static string Name(string title, string fallback) => string.IsNullOrWhiteSpace(title) ? fallback : title;
}
