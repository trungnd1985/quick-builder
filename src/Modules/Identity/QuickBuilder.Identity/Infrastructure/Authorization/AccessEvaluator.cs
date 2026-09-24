using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Infrastructure.Authorization;

/// <summary>
/// What the current request's user may do (research R-05).
/// </summary>
/// <remarks>
/// Grants are read from the database on first use and memoised for the rest of the request
/// only. Nothing is cached across requests, so a grant change applies to the user's very
/// next request on every instance (FR-017).
/// </remarks>
internal sealed class AccessEvaluator(
    IHttpContextAccessor httpContextAccessor,
    IdentityDbContext database,
    CapabilityCatalog catalog) : IAccessEvaluator
{
    private List<(SystemRole Role, Guid? SiteId)>? _grants;

    public bool IsSignedIn => UserId.HasValue;

    public Guid? UserId => IdentityClaims.GetUserId(httpContextAccessor.HttpContext?.User);

    public async Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) =>
        (await LoadGrantsAsync(cancellationToken).ConfigureAwait(false))
            .Exists(g => g.Role == SystemRole.Administrator);

    public async Task<bool> HasCapabilityAsync(string name, Guid? siteId, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Get(name);
        var grants = await LoadGrantsAsync(cancellationToken).ConfigureAwait(false);

        // FR-021: Administrator holds every capability everywhere.
        if (grants.Exists(g => g.Role == SystemRole.Administrator))
        {
            return true;
        }

        // Installation-scoped capabilities are held only by Administrator in this feature:
        // site roles never reach installation scope (FR-020).
        if (definition.Scope == CapabilityScope.Installation || siteId is null)
        {
            return false;
        }

        return grants.Exists(g => g.SiteId == siteId && definition.GrantedTo.Contains(g.Role));
    }

    public async Task<SiteVisibility> GetVisibleSitesAsync(CancellationToken cancellationToken = default)
    {
        var grants = await LoadGrantsAsync(cancellationToken).ConfigureAwait(false);

        if (grants.Exists(g => g.Role == SystemRole.Administrator))
        {
            return SiteVisibility.Everything;
        }

        var sites = grants
            .Where(g => g.SiteId.HasValue)
            .Select(g => g.SiteId!.Value)
            .ToHashSet();

        return sites.Count == 0 ? SiteVisibility.Nothing : new SiteVisibility(false, sites);
    }

    private async Task<List<(SystemRole Role, Guid? SiteId)>> LoadGrantsAsync(CancellationToken cancellationToken)
    {
        if (_grants is not null)
        {
            return _grants;
        }

        if (UserId is not { } userId)
        {
            _grants = [];
            return _grants;
        }

        var rows = await database.RoleGrants
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => new { g.Role, g.SiteId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _grants = rows.ConvertAll(r => (r.Role, r.SiteId));
        return _grants;
    }
}
