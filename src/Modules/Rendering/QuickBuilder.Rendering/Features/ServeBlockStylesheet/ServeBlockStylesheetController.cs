using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.ServeBlockStylesheet;

/// <summary>
/// Public surface: the block stylesheet published pages link to (012 research PR-10,
/// public-rendering.md §5).
/// </summary>
/// <remarks>
/// Its name carries a hash of its bytes, so it is cached for a year; any other name under
/// <c>/_assets</c> gets the site's not-found page. The same file for every site.
/// </remarks>
[ApiController]
// Public surface: site visitors never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
public sealed class ServeBlockStylesheetController(ICurrentSite currentSite, IPublicNotFoundPage notFound) : ControllerBase
{
    [HttpGet(RenderingRoutes.AssetsPrefix + "/{**file}")]
    [HttpHead(RenderingRoutes.AssetsPrefix + "/{**file}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string? file, CancellationToken cancellationToken)
    {
        var stylesheet = BlockStylesheet.Instance;

        if (!string.Equals(file, stylesheet.FileName, StringComparison.Ordinal))
        {
            return currentSite.Site is { } site
                ? new PublicResponseResult(await notFound.RenderAsync(site, cancellationToken).ConfigureAwait(false))
                : NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stylesheet.Content, "text/css; charset=utf-8");
    }
}
