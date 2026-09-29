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
    /// when no page has that address or it is not live. One indexed lookup.
    /// </summary>
    Task<LivePageRef?> FindLiveByPathAsync(Guid siteId, string path, CancellationToken cancellationToken = default);

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
}

/// <summary>A live page found by address, with the generation its cache key carries (PR-05).</summary>
public sealed record LivePageRef(Guid PageId, long Generation);

/// <summary>A live component document.</summary>
public sealed record LiveDocument(int SchemaVersion, JsonElement Document);

/// <summary>A live page version: its document and the title visitors see.</summary>
public sealed record LivePageVersion(int SchemaVersion, JsonElement Document, string Title);

/// <summary>A page a link points at: where it is now, and whether visitors can open it.</summary>
public sealed record PageLinkTarget(string Path, bool IsLive);
