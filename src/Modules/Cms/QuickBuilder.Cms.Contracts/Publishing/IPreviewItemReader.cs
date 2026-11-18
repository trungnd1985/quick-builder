namespace QuickBuilder.Cms.Contracts.Publishing;

/// <summary>
/// Item drafts and retained versions, for previews only (022 research PV-02,
/// module-surface.md §2.2).
/// </summary>
/// <remarks>
/// Never used by the public responder: architecture tests keep it out of the public path, so
/// visitors still read only published items (constitution Principle VII). The Cms module
/// implements it over its own read-only preview context. The site and trash filters apply, and
/// an item of a deleted collection is gone with it. Values come at the current version.
/// </remarks>
public interface IPreviewItemReader
{
    /// <summary>An item's draft in the shape bindings read; null when the item is not readable.</summary>
    Task<PreviewItem?> GetDraftAsync(Guid siteId, Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>A retained published version of an item; null when the item or the version is gone (015 FR-023).</summary>
    Task<PreviewItemVersion?> GetVersionAsync(Guid siteId, Guid itemId, int number, CancellationToken cancellationToken = default);

    /// <summary>Current names of items and their collections, for the site-wide link list (FR-015).</summary>
    Task<IReadOnlyDictionary<Guid, PreviewItemName>> GetNamesAsync(
        Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// An item's draft. <see cref="Item"/> is built from the draft values and the draft slug, in
/// the shape bindings read (016 research TB-09); its values are content: render them encoded.
/// </summary>
/// <param name="NeedsAttention">A required value is missing or a constraint unmet (015); the preview still shows it (PV-15).</param>
/// <param name="PendingScheduleAtUtc">When a pending scheduled publish is due; null when there is none.</param>
public sealed record PreviewItem(
    PublishedItem Item,
    DateTimeOffset SavedAtUtc,
    Guid? SavedBy,
    bool NeedsAttention,
    DateTimeOffset? PendingScheduleAtUtc,
    bool IsPublished);

/// <summary>A retained published version of an item, in the shape bindings read.</summary>
public sealed record PreviewItemVersion(PublishedItem Item, int Number, DateTimeOffset PublishedAtUtc, Guid PublishedBy);

/// <summary>An item's collection and its current name (content: encode it).</summary>
public sealed record PreviewItemName(Guid CollectionId, string? Name);
