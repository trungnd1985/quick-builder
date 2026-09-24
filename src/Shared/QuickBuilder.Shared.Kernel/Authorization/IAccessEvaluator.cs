namespace QuickBuilder.Shared.Kernel.Authorization;

/// <summary>
/// What the current request's user may do, for handlers that need more than yes/no —
/// typically to filter a list (contracts/authorization.md §6 rule 3).
/// </summary>
/// <remarks>
/// Implemented by the Identity module. Grants are read per request and never cached across
/// requests, so a permission change applies to the user's very next request (FR-017).
/// </remarks>
public interface IAccessEvaluator
{
    bool IsSignedIn { get; }

    Guid? UserId { get; }

    /// <summary>Whether the user holds the installation-wide Administrator role.</summary>
    /// <remarks>
    /// Asynchronous because grants are loaded from the database on first use in a request,
    /// then reused for the rest of it.
    /// </remarks>
    Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the user holds the capability — at <paramref name="siteId"/> when the
    /// capability is site-scoped, ignored otherwise.
    /// </summary>
    Task<bool> HasCapabilityAsync(string name, Guid? siteId, CancellationToken cancellationToken = default);

    /// <summary>The sites the user may see: all of them, or only those listed.</summary>
    Task<SiteVisibility> GetVisibleSitesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Which sites a user may see. <see cref="All"/> is true for administrators, in which case
/// <see cref="SiteIds"/> is empty and meaningless.
/// </summary>
public sealed record SiteVisibility(bool All, IReadOnlySet<Guid> SiteIds)
{
    public static SiteVisibility Everything { get; } = new(true, new HashSet<Guid>());

    public static SiteVisibility Nothing { get; } = new(false, new HashSet<Guid>());

    public bool CanSee(Guid siteId) => All || SiteIds.Contains(siteId);
}
