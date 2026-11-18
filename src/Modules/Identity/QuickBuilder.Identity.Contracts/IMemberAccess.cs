namespace QuickBuilder.Identity.Contracts;

/// <summary>
/// Whether another user may act on a site now (022 research PV-08, module-surface.md §2.4).
/// </summary>
/// <remarks>
/// For checks about someone who is not the caller: a preview link keeps working only while its
/// creator may still edit what it shows (spec FR-009, clarification 2). Grants and status are
/// read on every call and never cached, so a removed role or a deactivated account takes effect
/// at the next request on every instance. Administrator holds every capability everywhere; a
/// deactivated user holds none.
/// </remarks>
public interface IMemberAccess
{
    Task<bool> HoldsCapabilityAsync(Guid userId, string capability, Guid siteId, CancellationToken cancellationToken = default);
}
