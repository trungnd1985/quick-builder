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

    /// <summary>Change the title suffix, default sharing image and organisation (017 FR-006). Designer.</summary>
    public const string SearchManage = "sites.search.manage";

    /// <summary>
    /// Change "Discourage search engines", the robots.txt rules and the host name redirect
    /// (017 FR-006, FR-014). Administrator only: a mistake removes the site from search.
    /// </summary>
    public const string SearchVisibilityManage = "sites.search_visibility.manage";

    /// <summary>
    /// Export a site, follow its exports and download the archives (020 FR-035). Administrator
    /// only: an archive holds every draft and setting of the site.
    /// </summary>
    public const string TransferExport = "sites.transfer.export";

    /// <summary>
    /// Upload, check and import archives, and act on any transfer by its id (020 FR-035).
    /// Installation-wide: an import may create a site, and <c>/api/transfers/…</c> routes have
    /// no site to scope by (020 analysis I3). Administrator only.
    /// </summary>
    public const string TransferImport = "sites.transfer.import";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(Create, CapabilityScope.Installation, []),
        new(View, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(ManageHostNames, CapabilityScope.Site, []),
        new(SettingsManage, CapabilityScope.Site, []),
        new(AvailabilityManage, CapabilityScope.Site, []),
        new(SearchManage, CapabilityScope.Site, [SystemRole.Designer]),
        new(SearchVisibilityManage, CapabilityScope.Site, []),
        new(TransferExport, CapabilityScope.Site, []),
        new(TransferImport, CapabilityScope.Installation, []),
    ];
}
