using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// The module's read surface for other modules and for site resolution (FR-005).
/// </summary>
/// <remarks>
/// Host-name lookup runs on every public request, so it is cached (D-09). The cache is
/// invalidated by the write paths that can change the mapping — creating a site and adding
/// a host name — in the same request that changes it.
/// <para>
/// Known limitation: the in-memory provider cannot invalidate across instances. Correct
/// for the single-instance reference deployment; the Redis provider spec must address it
/// before multi-instance is advertised to operators (research.md D-09).
/// </para>
/// </remarks>
internal sealed class SiteDirectory(SitesDbContext database, ICacheProvider cache) : ISiteDirectory
{
    private const string Category = "site-by-host";

    public async Task<SiteDto?> FindByHostNameAsync(string hostName, CancellationToken cancellationToken = default)
    {
        var normalised = SiteHostName.Normalise(hostName);
        if (!SiteHostName.IsValid(normalised))
        {
            return null;
        }

        return await cache.GetOrCreateAsync(
            CacheKey.Global(Category, normalised),
            ct => LoadByHostNameAsync(normalised, ct),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<SiteDto?> FindByIdAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        // Cross-site read: the caller names the site explicitly, and at this point no site
        // is resolved for the request, so the ambient filter would match nothing.
        var site = await database.Sites
            .AsNoTracking()
            .AcrossAllSites("Looking up a site by its id, before any site is resolved for the request.")
            .Where(s => s.Id == siteId)
            .Select(s => new SiteDto(
                s.Id,
                s.Name,
                s.HostNames.Select(h => h.HostName).ToList(),
                s.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return site;
    }

    private async Task<SiteDto?> LoadByHostNameAsync(string normalisedHostName, CancellationToken cancellationToken)
    {
        // Cross-site by necessity: this query is what *determines* the site, so it cannot
        // be scoped to one. This is the clearest legitimate case for the bypass.
        var siteId = await database.HostNames
            .AsNoTracking()
            .AcrossAllSites("Resolving a host name to a site is what decides which site the request belongs to.")
            .Where(h => h.HostName == normalisedHostName)
            .Select(h => h.SiteId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return siteId == Guid.Empty
            ? null
            : await FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops the cached mapping for a host name after it changes.</summary>
    internal void InvalidateHostName(string normalisedHostName) =>
        cache.Remove(CacheKey.Global(Category, normalisedHostName));
}
