using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Cms;

/// <summary>
/// What the Cms module lets people do, by name (014 research CS-15; 002 contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Administrators and Designers shape the content model; Editors, who write content, see it
/// (spec Assumptions). Registered from <see cref="CmsModule.AddCmsModule"/>; Identity enforces
/// them without knowing this module exists. Administrator holds all of them everywhere and is
/// never listed. The names never change once shipped.
/// </remarks>
internal static class CmsCapabilities
{
    /// <summary>See collections, their schemas, history and previews. Every site role.</summary>
    public const string CollectionsView = "cms.collections.view";

    /// <summary>Create, save and delete collections. Designer.</summary>
    public const string CollectionsManage = "cms.collections.manage";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(CollectionsView, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(CollectionsManage, CapabilityScope.Site, [SystemRole.Designer]),
    ];
}
