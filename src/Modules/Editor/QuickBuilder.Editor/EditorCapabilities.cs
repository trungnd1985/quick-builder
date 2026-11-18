using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Editor;

/// <summary>
/// What the Editor module lets people do, by name (011 research ED-20; 002 contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Every site role that can see pages can see the tokens, because the editor's canvas draws
/// with them; only Designer changes them (011 FR-040). Registered from
/// <see cref="EditorModule.AddEditorModule"/>; Identity enforces them without knowing this
/// module exists. Administrator holds both everywhere and is never listed. The names never
/// change once shipped.
/// </remarks>
internal static class EditorCapabilities
{
    /// <summary>Read the site's design tokens. Designer and Editor.</summary>
    public const string DesignTokensView = "editor.design_tokens.view";

    /// <summary>Change the site's design tokens. Designer.</summary>
    public const string DesignTokensManage = "editor.design_tokens.manage";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(DesignTokensView, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(DesignTokensManage, CapabilityScope.Site, [SystemRole.Designer]),

        // 022 FR-015: every preview link of a site, listed and revoked. Administrator only.
        new(QuickBuilder.Editor.Contracts.PreviewCapabilities.Manage, CapabilityScope.Site, []),
    ];
}
