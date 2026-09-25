using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// The module's read surface for other modules and for site resolution (FR-005).
/// </summary>
/// <remarks>
/// <b>No cache across requests</b> (009 research S-01). Until 009 host-name lookups were
/// cached in memory for five minutes, which an in-memory cache cannot invalidate on other
/// instances — exception E-4. Once host names could be removed and sites disabled, that
/// would have kept a removed name or a disabled site working on every other instance for
/// minutes (FR-012). Resolution is one probe on the unique host-name index instead, and a
/// change is visible everywhere at the next request.
/// <para>
/// <b>Within a request</b> the site resolution middleware loads the site once, and this
/// scoped instance keeps it, so <see cref="ICurrentSite"/> and <see cref="FindByIdAsync"/>
/// for the same site cost no second query (research S-02).
/// </para>
/// <para>
/// Resolution is a plain LINQ query rather than a compiled one: the cross-site bypass is
/// a method EF cannot see inside a compiled query's expression, and EF's own query cache
/// already avoids recompiling it.
/// </para>
/// </remarks>
internal sealed class SiteDirectory(SitesDbContext database, SiteDtoMapper mapper) : ISiteDirectory
{
    private readonly Dictionary<Guid, SiteDto> _loaded = [];

    public async Task<SiteDto?> FindByHostNameAsync(string hostName, CancellationToken cancellationToken = default)
    {
        if (!HostNames.TryNormalise(hostName, out var normalised))
        {
            return null;
        }

        // Cross-site by necessity: this query is what *decides* the site, so it cannot be
        // scoped to one. Filtering on status here, in the same statement, is what makes a
        // disabled site's host names indistinguishable from unknown ones, down to the
        // timing (research S-03).
        var row = await database.Sites
            .AsNoTracking()
            .AcrossAllSites("Resolving a host name to a site is what decides which site the request belongs to.")
            .Where(s => s.Status == SiteStatus.Enabled && s.HostNames.Any(h => h.HostName == normalised))
            .Select(SiteDtoMapper.Projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Remember(mapper.ToDto(row));
    }

    public async Task<SiteDto?> FindByIdAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        if (_loaded.TryGetValue(siteId, out var known))
        {
            return known;
        }

        // Cross-site: the caller names the site explicitly, and at this point no site is
        // resolved for the request, so the ambient filter would match nothing.
        var row = await database.Sites
            .AsNoTracking()
            .AcrossAllSites("Looking up a site by its id, before any site is resolved for the request.")
            .Where(s => s.Id == siteId)
            .Select(SiteDtoMapper.Projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Remember(mapper.ToDto(row));
    }

    public async Task<bool> IsHostNameTakenAsync(string hostName, CancellationToken cancellationToken = default)
    {
        if (!HostNames.TryNormalise(hostName, out var normalised))
        {
            return false;
        }

        // Cross-site by definition: the question is whether *any* site holds the name.
        return await database.HostNames
            .AsNoTracking()
            .AcrossAllSites("Checking whether any site holds a host name, before any site is resolved.")
            .AnyAsync(h => h.HostName == normalised, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The site resolved or loaded earlier in this request, if it was.</summary>
    internal SiteDto? Loaded(Guid siteId) => _loaded.GetValueOrDefault(siteId);

    /// <summary>
    /// Forgets a site after this request changed it, so a later read in the same request
    /// sees the change.
    /// </summary>
    internal void Forget(Guid siteId) => _loaded.Remove(siteId);

    private SiteDto Remember(SiteDto site)
    {
        _loaded[site.Id] = site;
        return site;
    }
}
