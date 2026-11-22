namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// The words of the preview bar and the fixed preview pages (022 research PV-13,
/// contracts/admin-preview.md §6, public-preview.md §3–§4). English only for now (spec
/// assumption "Bar language and times"); translations are in docs/backlog.md.
/// </summary>
/// <remarks>
/// Times are written in UTC with the suffix " UTC", because a page without scripts cannot know
/// the viewer's time zone.
/// </remarks>
internal static class PreviewTexts
{
    public const string Language = "en";

    // The bar.
    public const string BarLabel = "Preview";
    public const string NotLive = "Preview — not live";
    public const string DraftSaved = "Draft saved {0}";
    public const string DraftSavedBy = "Draft saved {0} by {1}";
    public const string VersionPublished = "Version {0}, published {1}";
    public const string VersionPublishedBy = "Version {0}, published {1} by {2}";
    public const string SampleItem = "Showing the item “{0}”";
    public const string FromDraft = "{0} shown from its draft (never published)";
    public const string Scheduled = "Scheduled to publish {0}";
    public const string NeedsAttention = "This item needs attention: some values may not show";
    public const string Expires = "This link expires {0}";
    public const string LinksLeave = "Links lead to the published site";
    public const string BackToEditor = "Back to editor";
    public const string SharePreview = "Share preview";
    public const string HideBar = "Hide bar";

    // What a part shown from its draft is called.
    public const string Component = "Shared component “{0}”";
    public const string Menu = "Menu “{0}”";
    public const string Design = "The site design";
    public const string Template = "Template “{0}”";
    public const string SampleItemPart = "Item “{0}”";

    // The fixed pages.
    public const string GoneTitle = "Preview not available";
    public const string GoneMessage = "This preview link is no longer available.";
    public const string LaterTitle = "Preview not available right now";
    public const string LaterMessage = "This preview is not available right now. Please try again later.";
    public const string TooManyTitle = "Too many attempts";
    public const string TooManyMessage = "Too many preview links were tried from your network. Please try again later.";
    public const string NoTemplateTitle = "No template page";
    public const string NoTemplateMessage = "This collection has no template page, so its items cannot be previewed as pages.";
    public const string InvalidDraftTitle = "This draft cannot be previewed";
    public const string InvalidDraftAdvice = "Fix it in the editor and preview again.";
    public const string FormTitle = "Preview";
    public const string FormMessage = "This is a preview. Forms are not sent from previews.";

    /// <summary>A time as the bar shows it: <c>2026-11-19 10:42 UTC</c>.</summary>
    public static string Time(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
}
