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
    public const string PageDraftsEdit = "pages.page_drafts.edit";

    /// <summary>Create, rename and delete components, and save their drafts. Designer.</summary>
    public const string ComponentsManage = "pages.components.manage";

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
    ];
}
