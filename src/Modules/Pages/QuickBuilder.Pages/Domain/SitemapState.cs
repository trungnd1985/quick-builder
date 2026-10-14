using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// The site's sitemap generation (017 research SE-15, data-model §1.3).
/// </summary>
/// <remarks>
/// Moves on in the transaction of every change to what the sitemap lists or the dates it
/// shows. The sitemap's cache key carries it, read before the content, as a page's key
/// carries its generation (012 PR-05).
/// </remarks>
internal sealed class SitemapState : ISiteOwned
{
    public SitemapState(Guid siteId)
    {
        SiteId = siteId;
        Generation = 1;
    }

    // EF Core materialisation.
    private SitemapState()
    {
    }

    public Guid SiteId { get; private set; }

    public long Generation { get; private set; }
}
