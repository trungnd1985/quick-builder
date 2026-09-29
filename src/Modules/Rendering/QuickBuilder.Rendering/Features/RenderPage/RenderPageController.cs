using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>Public surface: a published page, at its address on its site's host names (012 FR-022).</summary>
/// <remarks>
/// The last route to match: <c>/_media</c>, <c>/_assets</c> and every administrative route are
/// more specific, and <see cref="PublicPathConstraint"/> keeps it off the administrative
/// prefixes entirely. Site resolution has already run, so an unknown or disabled host never
/// gets here (009).
/// </remarks>
[ApiController]
// Public surface: site visitors never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
public sealed class RenderPageController(IDispatcher dispatcher, ICurrentSite currentSite) : ControllerBase
{
    private const string Route = "{**path:" + PublicPathConstraint.Name + "}";

    [HttpGet(Route, Order = int.MaxValue)]
    [HttpHead(Route, Order = int.MaxValue)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (currentSite.Site is not { } site)
        {
            return NotFound();
        }

        var response = await dispatcher
            .QueryAsync(new RenderPageQuery(site, Request.Path.Value, Request.PathBase.Value ?? string.Empty), cancellationToken)
            .ConfigureAwait(false);

        return new PublicResponseResult(response);
    }

    /// <summary>Pages are only read: anything but GET and HEAD is refused (public-rendering.md §1).</summary>
    [HttpPost(Route, Order = int.MaxValue)]
    [HttpPut(Route, Order = int.MaxValue)]
    [HttpPatch(Route, Order = int.MaxValue)]
    [HttpDelete(Route, Order = int.MaxValue)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    public IActionResult OtherMethod()
    {
        Response.Headers.Allow = "GET, HEAD";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }
}

/// <summary>Writes a <see cref="PublicResponse"/> exactly: status, headers, and the body unless HEAD.</summary>
internal sealed class PublicResponseResult(PublicResponse response) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var http = context.HttpContext;
        http.Response.StatusCode = response.StatusCode;
        http.Response.ContentType = response.ContentType;

        foreach (var (name, value) in response.Headers)
        {
            http.Response.Headers[name] = value;
        }

        http.Response.ContentLength = response.Body.Length;

        if (!HttpMethods.IsHead(http.Request.Method) && response.Body.Length > 0)
        {
            await http.Response.Body.WriteAsync(response.Body, http.RequestAborted).ConfigureAwait(false);
        }
    }
}
