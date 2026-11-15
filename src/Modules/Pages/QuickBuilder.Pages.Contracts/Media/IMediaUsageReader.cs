namespace QuickBuilder.Pages.Contracts.Media;

/// <summary>
/// Where a site's media files are used by pages and components (013 contracts/readers.md §2.1,
/// research MV-05).
/// </summary>
/// <remarks>
/// The Pages module keeps its references in the same transaction as every draft save,
/// publish and delete, so what this returns is never stale. The Media module asks it before
/// deleting or replacing a file, and to show usage in the library. It never caches it.
/// </remarks>
public interface IMediaUsageReader
{
    /// <summary>
    /// Place counts for each of <paramref name="mediaIds"/>. Files with no usage are present
    /// with zeros. One query, for up to a page of files.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MediaUsageSummary>> SummariseAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default);

    /// <summary>Every page and component of the site that uses the file, pages first, each group by title.</summary>
    Task<IReadOnlyList<MediaUsagePlace>> ListAsync(
        Guid siteId, Guid mediaId, CancellationToken cancellationToken = default);
}

/// <summary>
/// How many places use a file. A place is one page or one component, however many versions
/// or blocks of it show the file.
/// </summary>
/// <param name="Places">Distinct pages and components using the file in any way.</param>
/// <param name="DraftPlaces">Places whose current draft shows it.</param>
/// <param name="LivePlaces">Places whose live published version shows it.</param>
/// <param name="HistoryOnlyPlaces">Places whose only use is in earlier published versions kept for rollback.</param>
/// <param name="TrashedPlaces">
/// Pages and components in the trash that use the file (021 FR-027). They are not counted in the
/// others, so they never make the file in use or live.
/// </param>
public sealed record MediaUsageSummary(int Places, int DraftPlaces, int LivePlaces, int HistoryOnlyPlaces, int TrashedPlaces = 0)
{
    /// <summary>No page or component uses the file.</summary>
    public static MediaUsageSummary None { get; } = new(0, 0, 0, 0);

    /// <summary>Visitors can see it: deleting or replacing it needs the publishing capability (013 clarifications).</summary>
    public bool InLiveUse => LivePlaces > 0;

    /// <summary>Anything uses it: deleting it needs the in-use confirmation (013 FR-015).</summary>
    public bool InUse => Places > 0;
}

/// <summary>One page or component that uses a file.</summary>
/// <param name="Kind"><c>page</c> or <c>component</c>.</param>
/// <param name="Id">The page's or component's id.</param>
/// <param name="Title">The page title or component name. Content: render as text, never log.</param>
/// <param name="InDraft">Its current draft shows the file.</param>
/// <param name="Live">Its live version shows the file.</param>
/// <param name="InHistory">An earlier version it keeps for rollback shows the file.</param>
/// <param name="LivePagesThroughComponent">For a live component, how many live pages show it; otherwise null.</param>
/// <param name="InTrash">The page or component is in the trash (021 FR-027): shown apart, never blocking.</param>
public sealed record MediaUsagePlace(
    string Kind,
    Guid Id,
    string Title,
    bool InDraft,
    bool Live,
    bool InHistory,
    int? LivePagesThroughComponent,
    bool InTrash = false);
