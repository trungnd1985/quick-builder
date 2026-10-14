using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Features.Sitemap;
using QuickBuilder.Rendering.Seo;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.Robots;

/// <summary>A request for a site's robots.txt.</summary>
internal sealed record RobotsQuery(SiteDto Site, string? IfNoneMatch) : IQuery<PublicResponse>;

/// <summary>
/// A site's robots.txt (017 FR-025, FR-027, research SE-16; public-seo.md §6).
/// </summary>
/// <remarks>
/// It depends only on the site's settings, so its key is the site's version: a change of
/// settings or host names gives every instance the new text at the next request. Generating
/// it is cheaper than caching it, so only the validator is kept.
/// </remarks>
internal sealed class RobotsHandler : IQueryHandler<RobotsQuery, PublicResponse>
{
    public Task<PublicResponse> HandleAsync(RobotsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var site = query.Site;

        var key = CacheKey.ForSite(site.Id, "robots", $"{site.Version}:{site.BaseUrl}:{RenderKey.RendererStamp}");
        var etag = RenderKey.ETagOf(key);
        var headers = SitemapHandler.PublicTextHeaders(etag, site);

        return Task.FromResult(RenderKey.Matches(etag, query.IfNoneMatch)
            ? new PublicResponse(StatusCodes.Status304NotModified, RobotsText.ContentType, headers, ReadOnlyMemory<byte>.Empty)
            : new PublicResponse(StatusCodes.Status200OK, RobotsText.ContentType, headers, Encoding.UTF8.GetBytes(RobotsText.For(site))));
    }
}

/// <summary>The text of a site's robots.txt.</summary>
internal static class RobotsText
{
    public const string ContentType = "text/plain; charset=utf-8";

    /// <summary>
    /// The generated rules, the sitemap's absolute address, then the Administrator's rules. With
    /// "Discourage search engines" on, everything is disallowed; the sitemap line stays.
    /// </summary>
    public static string For(SiteDto site)
    {
        ArgumentNullException.ThrowIfNull(site);

        var text = new StringBuilder();
        text.Append("User-agent: *\n");
        if (site.Search.Discouraged)
        {
            text.Append("Disallow: /\n");
        }
        else
        {
            text.Append("Disallow: /api/\n");
            text.Append("Disallow: /admin/\n");
            text.Append("Allow: /\n");
        }

        text.Append('\n').Append("Sitemap: ").Append(SiteAddress.Absolute(site.BaseUrl, "/sitemap.xml")).Append('\n');

        if (!string.IsNullOrWhiteSpace(site.Search.RobotsRules))
        {
            text.Append('\n').Append("# Custom rules\n").Append(site.Search.RobotsRules).Append('\n');
        }

        return text.ToString();
    }
}

/// <summary>Public surface: robots.txt.</summary>
[ApiController]
// Public surface: crawlers never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
public sealed class RobotsController(IDispatcher dispatcher, ICurrentSite currentSite) : ControllerBase
{
    [HttpGet("robots.txt")]
    [HttpHead("robots.txt")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (currentSite.Site is not { } site)
        {
            return NotFound();
        }

        var response = await dispatcher.QueryAsync(new RobotsQuery(site, Request.Headers.IfNoneMatch.ToString()), cancellationToken).ConfigureAwait(false);
        return new PublicResponseResult(response);
    }
}
