namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// The Pages content capability other modules ask about, by name (013 contracts/readers.md §2.2).
/// </summary>
/// <remarks>
/// Registered by the Pages module (010 research P-13). It lives in the contract because the
/// Media module names the pages and components that use a file only to people who may see
/// them (013 FR-014). The name has not changed since 010, and never will: it is part of what
/// a role means.
/// </remarks>
public static class PageContentCapabilities
{
    /// <summary>See the page tree, pages, drafts, components and their usages. Every site role.</summary>
    public const string View = "pages.content.view";
}
