using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Sites;

/// <summary>
/// What the Sites module lets people do, by name (002 contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Registered from <see cref="SitesModule.AddSitesModule"/>; Identity enforces them without
/// knowing this module exists. Administrator holds all of them everywhere. The names never
/// change once shipped: they are part of what a role means.
/// </remarks>
internal static class SitesCapabilities
{
    /// <summary>Create a site. Installation-wide: only Administrators create sites (FR-027).</summary>
    public const string Create = "sites.create";

    /// <summary>See one site. Every site role holds it for the sites it is granted on.</summary>
    public const string View = "sites.view";

    /// <summary>Add host names. Administrator only in this feature (FR-027).</summary>
    public const string ManageHostNames = "sites.host_names.manage";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(Create, CapabilityScope.Installation, []),
        new(View, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(ManageHostNames, CapabilityScope.Site, []),
    ];
}
