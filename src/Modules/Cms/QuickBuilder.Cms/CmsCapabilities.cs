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
    public const string CollectionsView = QuickBuilder.Cms.Contracts.ItemCapabilities.CollectionsView;

    /// <summary>Create, save and delete collections. Designer.</summary>
    public const string CollectionsManage = "cms.collections.manage";

    /// <summary>
    /// Create, save, duplicate and delete items, and copy a version into the draft. Every site
    /// role (015 clarification Q1, research IT-16).
    /// </summary>
    public const string ItemsEdit = QuickBuilder.Cms.Contracts.ItemCapabilities.ItemsEdit;

    /// <summary>
    /// Publish, unpublish, schedule and roll back items. Every site role (015 clarification Q1);
    /// kept apart from <see cref="ItemsEdit"/> so who may change what visitors see can be
    /// separated later without renaming a shipped capability.
    /// </summary>
    public const string ItemsPublish = "cms.items.publish";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(CollectionsView, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(CollectionsManage, CapabilityScope.Site, [SystemRole.Designer]),
        new(ItemsEdit, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(ItemsPublish, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
    ];
}
