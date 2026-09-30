namespace QuickBuilder.Pages.Contracts.Publishing;

/// <summary>
/// The publishing capabilities, by name (012 research PR-18; 002 contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Registered by the Pages module. They are in the contract because the Editor module
/// requires <see cref="Manage"/> too, for publishing the design: one capability covers
/// every publishing action (clarification Q3 of specify). The names never change once shipped.
/// </remarks>
public static class PublishingCapabilities
{
    /// <summary>
    /// Publish, unpublish, roll back and schedule pages and components, and publish or roll
    /// back the design. Designer; not Editor.
    /// </summary>
    public const string Manage = "pages.publishing.manage";

    /// <summary>Read the site's publish log (FR-017). Administrator only.</summary>
    public const string LogView = "pages.publish_log.view";
}
