using System.Text.Json;

namespace QuickBuilder.Pages.Contracts.Publishing;

/// <summary>
/// Drafts and retained versions of pages, components and menus, for previews only (022
/// research PV-02, module-surface.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Never used by the public responder: architecture tests keep it out of the public path, so
/// visitors still read only published data (constitution Principle VII). The Pages module
/// implements it over its own read-only preview context, separate from the published one.
/// </para>
/// <para>
/// The site and trash filters apply: a page in the trash, or of another site, is absent.
/// Documents come at the current schema version, as published ones do (019 FR-025).
/// </para>
/// </remarks>
public interface IPreviewContentReader
{
    /// <summary>A page's saved draft, with what the preview bar shows; null when the page is not readable.</summary>
    Task<PreviewPage?> GetPageDraftAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A retained published version of a page; null when the page is not readable or the
    /// version's content is no longer kept (012 FR-013).
    /// </summary>
    Task<PreviewPageVersion?> GetPageVersionAsync(Guid siteId, Guid pageId, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// The drafts of those of <paramref name="componentIds"/> that have never been published
    /// (spec clarification Q2): a component with a live version, or not readable, is absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PreviewDraftDocument>> GetUnpublishedComponentDraftsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken = default);

    /// <summary>The drafts of those of <paramref name="menuIds"/> that have never been published, with their names.</summary>
    Task<IReadOnlyDictionary<Guid, PreviewDraftDocument>> GetUnpublishedMenuDraftsAsync(
        Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken cancellationToken = default);

    /// <summary>The template page of <paramref name="collectionId"/>, live or not, with its draft; null when there is none.</summary>
    Task<PreviewPage?> GetTemplateDraftAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default);

    /// <summary>The live version of a template page, for an item preview; null when it is not live.</summary>
    Task<PreviewPageVersion?> GetLiveTemplateVersionAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Current titles of pages, for the site-wide link list (FR-015). Absent ids are not pages of the site.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTitlesAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// A page's draft as a preview shows it. <see cref="Title"/>, <see cref="Path"/> and
/// <see cref="Document"/> are content: render them encoded, never log them.
/// </summary>
/// <param name="Path">The page's address; null for a template page, which has none of its own.</param>
/// <param name="CollectionId">For a template page, the collection whose items it shows.</param>
/// <param name="PendingScheduleAtUtc">When a pending scheduled publish is due; null when there is none.</param>
/// <param name="IsLive">Whether the page has a live version.</param>
public sealed record PreviewPage(
    Guid PageId,
    string Title,
    string? Path,
    bool IsTemplate,
    Guid? CollectionId,
    int SchemaVersion,
    JsonElement Document,
    DateTimeOffset SavedAtUtc,
    Guid SavedBy,
    DateTimeOffset? PendingScheduleAtUtc,
    bool IsLive);

/// <summary>A retained published version of a page, at the current schema version.</summary>
/// <param name="Path">The page's current address; null for a template page.</param>
public sealed record PreviewPageVersion(
    Guid PageId,
    int Number,
    string Title,
    string? Path,
    int SchemaVersion,
    JsonElement Document,
    DateTimeOffset PublishedAtUtc,
    Guid PublishedBy);

/// <summary>A never-published component's or menu's draft, with its name (content: encode it).</summary>
public sealed record PreviewDraftDocument(int SchemaVersion, JsonElement Document, string Name);
