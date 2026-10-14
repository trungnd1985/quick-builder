using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Moves the site's sitemap generation on (017 research SE-15), in the transaction of the
/// change: a publish, unpublish or rollback, an address change, a template change, a delete,
/// or an item event Pages handles.
/// </summary>
/// <remarks>
/// The sitemap's cache key carries the generation, read before the content, so the next
/// request on every instance builds the sitemap again. The row is created on a site's first
/// bump. Bumping too often costs one rebuild on the next sitemap request; missing a bump would
/// show a stale sitemap, so callers bump on every change that might matter.
/// </remarks>
internal sealed class SitemapGeneration(PagesDbContext database)
{
    /// <summary>
    /// Moves the generation on in the caller's transaction. A site without a row yet gets one
    /// staged on the context, and the result is true: a caller that has already saved its change
    /// saves again.
    /// </summary>
    public async Task<bool> BumpAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var updated = await database.ForSite<SitemapState>(siteId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Generation, x => x.Generation + 1), cancellationToken)
            .ConfigureAwait(false);
        if (updated > 0 || database.ChangeTracker.Entries<SitemapState>().Any(e => e.Entity.SiteId == siteId))
        {
            return false;
        }

        database.SitemapStates.Add(new SitemapState(siteId));
        return true;
    }
}
