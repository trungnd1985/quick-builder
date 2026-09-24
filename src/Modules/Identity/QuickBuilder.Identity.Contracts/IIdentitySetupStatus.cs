namespace QuickBuilder.Identity.Contracts;

/// <summary>
/// Whether this installation has been claimed by its first administrator (spec FR-001).
/// </summary>
/// <remarks>
/// For other modules — a future <c>Setup</c> module, for example (research R-01) — that need
/// to know whether setup is complete. Once true it stays true: an installation is claimed
/// once and never unclaimed.
/// </remarks>
public interface IIdentitySetupStatus
{
    Task<bool> IsClaimedAsync(CancellationToken cancellationToken = default);
}
