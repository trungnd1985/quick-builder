using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Infrastructure.Authorization;

/// <summary>
/// Whether another user may act on a site now (022 research PV-08): the same rules as
/// <see cref="AccessEvaluator"/>, for a user who is not the caller.
/// </summary>
/// <remarks>
/// One query for the user's status and grants, on every call: nothing is cached, so a role
/// removed or an account deactivated takes effect at the next request on every instance
/// (002 FR-017).
/// </remarks>
internal sealed class MemberAccess(IdentityDbContext database, CapabilityCatalog catalog) : IMemberAccess
{
    public async Task<bool> HoldsCapabilityAsync(Guid userId, string capability, Guid siteId, CancellationToken cancellationToken = default)
    {
        var definition = catalog.Get(capability);

        var active = await database.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken)
            .ConfigureAwait(false);
        if (!active)
        {
            return false;
        }

        var grants = await database.RoleGrants
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => new { g.Role, g.SiteId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 002 FR-021: Administrator holds every capability everywhere.
        if (grants.Exists(g => g.Role == SystemRole.Administrator))
        {
            return true;
        }

        return definition.Scope == CapabilityScope.Site
            && grants.Exists(g => g.SiteId == siteId && definition.GrantedTo.Contains(g.Role));
    }
}
