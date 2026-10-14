using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.Sitemap;

/// <summary>A request for one of a site's sitemap files.</summary>
internal sealed record SitemapQuery(SiteDto Site, string File, string BasePath, string? IfNoneMatch) : IQuery<PublicResponse>;

/// <summary>
/// A site's <c>sitemap.xml</c> and numbered sitemap files (017 FR-023, FR-024, FR-026;
/// research SE-14, SE-15; public-seo.md §5).
/// </summary>
/// <remarks>
/// The key first, the content after (012 PR-05): the sitemap generation and the site's
/// version are read, then the whole set is built once per key and cached, so every file of
/// one generation agrees. A file the set does not hold gets the site's not-found page.
/// </remarks>
internal sealed class SitemapHandler(ISitemapReader pages, SitemapBuilder builder, IPublicNotFoundPage notFound, ICacheProvider cache)
    : IQueryHandler<SitemapQuery, PublicResponse>
{
    private static readonly TimeSpan Sliding = TimeSpan.FromHours(1);

    private const string ContentType = "application/xml; charset=utf-8";

    public async Task<PublicResponse> HandleAsync(SitemapQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var site = query.Site;

        var generation = await pages.GetGenerationAsync(site.Id, cancellationToken).ConfigureAwait(false);
        var key = CacheKey.ForSite(site.Id, "sitemap", $"{generation}:{site.Version}:{query.BasePath}:{RenderKey.RendererStamp}");
        var etag = RenderKey.ETagOf(key + ":" + query.File);

        var set = await cache.GetOrCreateAsync(
                key,
                ct => builder.BuildAsync(site, ct),
                built => new CacheEntrySettings(built.Size, Sliding),
                cancellationToken)
            .ConfigureAwait(false);

        if (!set.Files.TryGetValue(query.File, out var bytes))
        {
            return await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false);
        }

        var headers = PublicTextHeaders(etag, site);
        return RenderKey.Matches(etag, query.IfNoneMatch)
            ? new PublicResponse(StatusCodes.Status304NotModified, ContentType, headers, ReadOnlyMemory<byte>.Empty)
            : new PublicResponse(StatusCodes.Status200OK, ContentType, headers, bytes);
    }

    /// <summary>The headers of a public file other than a page: validated on every use, and not indexed when the site is hidden.</summary>
    internal static Dictionary<string, string> PublicTextHeaders(string etag, SiteDto site)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ETag"] = etag,
            ["Cache-Control"] = "public, no-cache",
            ["X-Content-Type-Options"] = "nosniff",
        };
        if (site.Search.Discouraged)
        {
            headers["X-Robots-Tag"] = "noindex, nofollow";
        }

        return headers;
    }
}

/// <summary>Public surface: the sitemap files.</summary>
[ApiController]
// Public surface: crawlers never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
public sealed class SitemapController(IDispatcher dispatcher, ICurrentSite currentSite) : ControllerBase
{
    [HttpGet("sitemap.xml")]
    [HttpHead("sitemap.xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public Task<IActionResult> IndexAsync(CancellationToken cancellationToken) =>
        ServeAsync(SitemapBuilder.IndexFile, cancellationToken);

    [HttpGet("sitemap-{number:int:min(1)}.xml")]
    [HttpHead("sitemap-{number:int:min(1)}.xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> FileAsync(int number, CancellationToken cancellationToken) =>
        ServeAsync($"sitemap-{number}.xml", cancellationToken);

    private async Task<IActionResult> ServeAsync(string file, CancellationToken cancellationToken)
    {
        if (currentSite.Site is not { } site)
        {
            return NotFound();
        }

        var response = await dispatcher.QueryAsync(
                new SitemapQuery(site, file, Request.PathBase.Value ?? string.Empty, Request.Headers.IfNoneMatch.ToString()),
                cancellationToken)
            .ConfigureAwait(false);
        return new PublicResponseResult(response);
    }
}
