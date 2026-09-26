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

    /// <summary>Add and remove host names. Administrator only (002 FR-027, 009 FR-022).</summary>
    public const string ManageHostNames = "sites.host_names.manage";

    /// <summary>Change name, scheme, language and primary host name (009 research S-12).</summary>
    public const string SettingsManage = "sites.settings.manage";

    /// <summary>
    /// Disable and enable a site. Kept apart from settings so that a future per-site
    /// administrator role can edit a site without being able to take it offline (S-12).
    /// </summary>
    public const string AvailabilityManage = "sites.availability.manage";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(Create, CapabilityScope.Installation, []),
        new(View, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(ManageHostNames, CapabilityScope.Site, []),
        new(SettingsManage, CapabilityScope.Site, []),
        new(AvailabilityManage, CapabilityScope.Site, []),
    ];
}
