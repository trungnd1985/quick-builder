using System.Text.Json;

namespace QuickBuilder.Pages.Contracts.Publishing;

/// <summary>
/// Published pages and components, as the public site reads them (012 contracts/readers.md §1.1).
/// </summary>
/// <remarks>
/// Only live versions are visible here, never drafts (constitution Principle VII). The Pages
/// module implements it over a read-only connection (research PR-06), so the public path
/// cannot write content even by mistake.
/// </remarks>
public interface IPublishedContentReader
{
    /// <summary>
    /// The live page at a normalised, lower-case address, with its render generation; null
    /// when no page has that address or it is not live. One indexed lookup. Template pages
    /// have no address of their own and are never found here (016 research TB-02).
    /// </summary>
    Task<LivePageRef?> FindLiveByPathAsync(Guid siteId, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live template page whose parent has the normalised, lower-case address
    /// <paramref name="parentPath"/>; null when there is none (016 research TB-03). One indexed
    /// lookup, on the template's pattern.
    /// </summary>
    Task<LiveTemplateRef?> FindLiveTemplateAsync(Guid siteId, string parentPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// The address patterns of the live templates of <paramref name="collectionIds"/>, for links
    /// to item pages; collections without a live template are absent (016 research TB-09).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetLiveTemplatePatternsAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default);

    /// <summary>The live version of a page, or null when it is not live.</summary>
    Task<LivePageVersion?> GetLivePageAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Live versions of the given components; ids without one are absent.</summary>
    Task<IReadOnlyDictionary<Guid, LiveDocument>> GetLiveComponentsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// For link targets: each page's current address and whether it is live. Ids that are not
    /// pages of the site are absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PageLinkTarget>> GetLinkTargetsAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken = default);

    /// <summary>Whether the site's home page is live, for the not-found page's link home.</summary>
    Task<bool> IsHomePageLiveAsync(Guid siteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live pages above <paramref name="pageId"/>, root first: the home page when it is live,
    /// then each live ancestor, with its live title (017 FR-034, research SE-18). One query.
    /// An ancestor that is not live is left out.
    /// </summary>
    Task<IReadOnlyList<LiveAncestor>> GetLiveAncestorsAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken = default);
}

/// <summary>A live page above another, for its breadcrumb trail (017 research SE-18).</summary>
/// <param name="Title">Its live version's title, as visitors see it.</param>
public sealed record LiveAncestor(Guid PageId, string Path, string Title, bool IsHome);

/// <summary>A live page found by address, with the generation its cache key carries (PR-05).</summary>
/// <param name="PageParamNames">
/// The page parameters of the live version's paginated lists, so the cache key can name the
/// pages visitors asked for before the document is read (016 research TB-11).
/// </param>
public sealed record LivePageRef(Guid PageId, long Generation, IReadOnlyList<string>? PageParamNames = null)
{
    /// <summary>The page parameters, none when the page has no paginated list.</summary>
    public IReadOnlyList<string> Params => PageParamNames ?? [];
}

/// <summary>A live template page found by its parent's address (016 research TB-03).</summary>
/// <param name="CollectionId">The collection whose items the template shows.</param>
public sealed record LiveTemplateRef(Guid PageId, long Generation, Guid CollectionId, IReadOnlyList<string>? PageParamNames = null)
{
    /// <summary>The page parameters, none when the template has no paginated list.</summary>
    public IReadOnlyList<string> Params => PageParamNames ?? [];
}

/// <summary>A live component document.</summary>
public sealed record LiveDocument(int SchemaVersion, JsonElement Document);

/// <summary>A live page version: its document and the title visitors see.</summary>
public sealed record LivePageVersion(int SchemaVersion, JsonElement Document, string Title);

/// <summary>A page a link points at: where it is now, and whether visitors can open it.</summary>
public sealed record PageLinkTarget(string Path, bool IsLive);
