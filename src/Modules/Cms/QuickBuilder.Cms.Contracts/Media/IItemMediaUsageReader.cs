namespace QuickBuilder.Cms.Contracts.Media;

/// <summary>
/// Where a site's media files are used by collection items (015 research IT-15,
/// module-surface.md §2).
/// </summary>
/// <remarks>
/// Cms keeps item media usage in the transaction of every item write and schema change, so what
/// this returns is never stale. The Media module adds it to the usage of pages and components:
/// in the library, before deleting or replacing a file, and in its live-use rule (013 MV-06).
/// </remarks>
public interface IItemMediaUsageReader
{
    /// <summary>Item counts for each of <paramref name="mediaIds"/>; files no item uses are present with zeros.</summary>
    Task<IReadOnlyDictionary<Guid, ItemMediaUsageSummary>> SummariseAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default);

    /// <summary>Every item of the site that uses the file, by collection name, then item name.</summary>
    Task<IReadOnlyList<ItemMediaUsagePlace>> ListAsync(Guid siteId, Guid mediaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The collections whose items' published values show the file, so the pages listing them
    /// can be refreshed when it changes (016 research TB-10).
    /// </summary>
    Task<IReadOnlyList<Guid>> CollectionsShowingPublishedAsync(Guid siteId, Guid mediaId, CancellationToken cancellationToken = default);
}

/// <summary>How many items use a file.</summary>
/// <param name="Items">Items using it in any way.</param>
/// <param name="DraftItems">Items whose draft shows it.</param>
/// <param name="LiveItems">Items whose published values show it: visitors can see it once items are rendered (#16).</param>
/// <param name="HistoryOnlyItems">Items whose only use is in versions kept for rollback.</param>
public sealed record ItemMediaUsageSummary(int Items, int DraftItems, int LiveItems, int HistoryOnlyItems, int TrashedItems = 0)
{
    public static ItemMediaUsageSummary None { get; } = new(0, 0, 0, 0);
}

/// <summary>One item that uses a file. <see cref="Name"/> and <see cref="CollectionName"/> are content: render as text, never log.</summary>
public sealed record ItemMediaUsagePlace(
    Guid ItemId,
    string? Name,
    string Slug,
    Guid CollectionId,
    string CollectionName,
    bool InDraft,
    bool Live,
    bool InScheduled,
    bool InHistory,
    bool InTrash = false);
