using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Pages;

/// <summary>
/// What the Pages module lets people do, by name (010 research P-13; 002 contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Designer builds, Editor writes (clarification Q3). Registered from
/// <see cref="PagesModule.AddPagesModule"/>; Identity enforces them without knowing this
/// module exists. Administrator holds all of them everywhere and is never listed. The names
/// never change once shipped: they are part of what a role means.
/// </remarks>
internal static class PagesCapabilities
{
    /// <summary>See the page tree, pages, drafts, components and their usages. Every site role.</summary>
    /// <remarks>Declared in the contract since 013, because Media asks for it too.</remarks>
    public const string ContentView = QuickBuilder.Pages.Contracts.PageContentCapabilities.View;

    /// <summary>Create, rename, move, delete pages and choose the home page. Designer.</summary>
    public const string StructureManage = "pages.structure.manage";

    /// <summary>Save a page's draft. Designer and Editor.</summary>
    public const string PageDraftsEdit = QuickBuilder.Pages.Contracts.PageContentCapabilities.DraftsEdit;

    /// <summary>Create, rename and delete components, and save their drafts. Designer.</summary>
    public const string ComponentsManage = QuickBuilder.Pages.Contracts.PageContentCapabilities.ComponentsManage;

    /// <summary>
    /// List, create, change, delete, import and export the site's redirects. Designer (017
    /// research SE-03). Offered redirects need only the capability of the change they come with.
    /// </summary>
    public const string RedirectsManage = "pages.redirects.manage";

    /// <summary>
    /// Create, rename, duplicate and delete menus, and save their drafts. Designer (024 research
    /// NV-18). Viewing needs <see cref="ContentView"/>; publishing, the publishing capability.
    /// </summary>
    public const string MenusManage = "pages.menus.manage";

    /// <summary>
    /// Delete trashed pages and components permanently, one, several or the whole trash.
    /// Administrators only (021 FR-021, research TR-14).
    /// </summary>
    public const string TrashPurge = "pages.trash.purge";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(ContentView, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(StructureManage, CapabilityScope.Site, [SystemRole.Designer]),
        new(PageDraftsEdit, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(ComponentsManage, CapabilityScope.Site, [SystemRole.Designer]),

        // 012: Designers publish, Editors prepare drafts (clarification Q3); the log is for
        // Administrators only (clarification Q2 of clarify).
        new(PublishingCapabilities.Manage, CapabilityScope.Site, [SystemRole.Designer]),
        new(PublishingCapabilities.LogView, CapabilityScope.Site, []),

        // 017: Designers shape the site's addresses, so they manage redirects.
        new(RedirectsManage, CapabilityScope.Site, [SystemRole.Designer]),

        // 024: menus shape the site's structure, so Designers build them ("Designer builds").
        new(MenusManage, CapabilityScope.Site, [SystemRole.Designer]),

        // 021 FR-021: only Administrators delete permanently.
        new(TrashPurge, CapabilityScope.Site, []),
    ];
}
