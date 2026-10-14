using QuickBuilder.Pages.Contracts.Redirects;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>A visitor's request for an address on a resolved site.</summary>
/// <param name="Site">The site the host name resolved to.</param>
/// <param name="Path">The request path, base path removed, as the server decoded it.</param>
/// <param name="BasePath">The installation's base path, prefixed to every address written.</param>
/// <param name="IfNoneMatch">The visitor's <c>If-None-Match</c> header, if any.</param>
/// <param name="Query">The query string's first value of each parameter; only the page's list parameters are read (016 TB-11).</param>
/// <param name="QueryPairs">Every parameter and value of the query, for matching redirects with a query (017 FR-017a).</param>
/// <param name="RawQuery">The query string as sent, with its <c>?</c>, carried to a redirect's target (017 public-seo.md §2).</param>
internal sealed record RenderPageQuery(
    SiteDto Site,
    string? Path,
    string BasePath,
    string? IfNoneMatch = null,
    IReadOnlyDictionary<string, string?>? Query = null,
    IReadOnlyList<KeyValuePair<string, string?>>? QueryPairs = null,
    string? RawQuery = null) : IQuery<PublicResponse>;

/// <summary>The page at the address, or the site's not-found page (public-rendering.md §1–§4).</summary>
internal sealed class RenderPageHandler(PageResponder responder, IPublicNotFoundPage notFound)
    : IQueryHandler<RenderPageQuery, PublicResponse>
{
    public async Task<PublicResponse> HandleAsync(RenderPageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return PathNormaliser.Normalise(query.Path) is { } path
            ? await responder.RespondAsync(
                query.Site,
                path,
                RawPathOf(query.Path),
                query.BasePath,
                query.IfNoneMatch,
                query.Query,
                query.QueryPairs is { Count: > 0 } pairs ? CanonicalQuery.FromPairs(pairs) : string.Empty,
                query.RawQuery,
                cancellationToken).ConfigureAwait(false)
            : await notFound.RenderAsync(query.Site, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The address as requested; the root, however written, is the root (017 SE-06).</summary>
    private static string RawPathOf(string? path) => string.IsNullOrEmpty(path) ? "/" : path;
}
