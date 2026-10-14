namespace QuickBuilder.Pages.Contracts.Redirects;

/// <summary>
/// The site's redirects, as the public site reads them (017 research SE-10, module-surface.md §2).
/// </summary>
/// <remarks>
/// Implemented by the Pages module over its read-only connection (012 research PR-06). Only
/// enabled redirects are visible. Nothing is cached: a change is visible on every instance at
/// the next request (FR-021).
/// </remarks>
public interface IRedirectReader
{
    /// <summary>
    /// The enabled exact redirect whose source is <paramref name="path"/> with exactly
    /// <paramref name="canonicalQuery"/>; null when none (FR-017a). One indexed lookup.
    /// </summary>
    /// <param name="path">The normalised, lower-case path.</param>
    /// <param name="canonicalQuery">The request's query in canonical form; never empty.</param>
    Task<RedirectMatch?> FindQueryAsync(Guid siteId, string path, string canonicalQuery, CancellationToken cancellationToken = default);

    /// <summary>
    /// The enabled exact redirect without a query whose source is <paramref name="path"/>,
    /// else the enabled prefix redirect with the longest source above it; null when none
    /// (FR-016, FR-033). One query.
    /// </summary>
    /// <param name="path">The normalised, lower-case path.</param>
    Task<RedirectMatch?> FindAsync(Guid siteId, string path, CancellationToken cancellationToken = default);
}

/// <summary>A redirect that answers a request, with what the responder needs to build <c>Location</c>.</summary>
/// <param name="RedirectId">For counting its use (<see cref="IRedirectHits"/>).</param>
/// <param name="StatusCode"><c>301</c> or <c>302</c>.</param>
/// <param name="FromQuerySource">Its source had a query, so the request's query is not carried over.</param>
/// <param name="TargetKind">What the target is.</param>
/// <param name="PageId">The target page, for <see cref="RedirectTargetKind.Page"/>.</param>
/// <param name="CollectionId">The target item's collection, for <see cref="RedirectTargetKind.Item"/>.</param>
/// <param name="ItemId">The target item, for <see cref="RedirectTargetKind.Item"/>.</param>
/// <param name="Path">The site path, with an optional query, for <see cref="RedirectTargetKind.Path"/>.</param>
/// <param name="Url">The absolute address, for <see cref="RedirectTargetKind.Url"/>.</param>
/// <param name="Remainder">
/// For a prefix redirect, the rest of the request path after its source, starting with
/// <c>/</c>; empty for an exact one.
/// </param>
public sealed record RedirectMatch(
    Guid RedirectId,
    int StatusCode,
    bool FromQuerySource,
    RedirectTargetKind TargetKind,
    Guid? PageId,
    Guid? CollectionId,
    Guid? ItemId,
    string? Path,
    string? Url,
    string Remainder);

/// <summary>What a redirect sends visitors to (017 research SE-09).</summary>
public enum RedirectTargetKind
{
    /// <summary>A page of the site, followed to its current address.</summary>
    Page,

    /// <summary>An item of the site, followed to its current address under its template.</summary>
    Item,

    /// <summary>A path on the site, as written.</summary>
    Path,

    /// <summary>An absolute <c>http</c> or <c>https</c> address, as written.</summary>
    Url,
}
