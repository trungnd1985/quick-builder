using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Host.Middleware;

/// <summary>
/// The two "not found" answers of the public surface (009 contracts/site-context.md §1,
/// 012 contracts/public-rendering.md §4).
/// </summary>
/// <remarks>
/// Neither echoes anything from the request — no host name, no path — so they confirm
/// nothing a probe could not already see.
/// <list type="bullet">
/// <item><see cref="WriteSiteNotResolvedAsync"/>: no enabled site holds the host name. It is
/// byte-identical for an unknown host and a disabled site (research S-03), and for an
/// installation with zero sites or a hundred (001 FR-014).</item>
/// <item><see cref="WritePublicNotFoundAsync"/>: the site resolved but has no such path. Since
/// 012 it is the site's not-found page, rendered by Rendering. An admin path on a host that is
/// not an admin host name gets exactly this too (S-13), which is what makes the administrative
/// surface invisible on client domains.</item>
/// </list>
/// Having one writer for each is what keeps the cases that must look alike from drifting
/// apart.
/// </remarks>
internal static class PublicResponses
{
    private const string SiteNotResolvedBody =
        """{"type":"https://quickbuilder.dev/errors/site-not-resolved","title":"Not found","status":404,"errorCode":"sites.not_resolved"}""";

    internal static Task WriteSiteNotResolvedAsync(HttpContext context) => WriteAsync(context, SiteNotResolvedBody);

    /// <summary>
    /// The site's not-found page, from Rendering (012 research PR-08, public-rendering.md §4).
    /// </summary>
    /// <remarks>
    /// Written for any public path nothing answers and for administrative paths on a client
    /// domain. Both go through this one call, so they stay byte-identical (009 research S-13).
    /// </remarks>
    internal static async Task WritePublicNotFoundAsync(HttpContext context, SiteDto site)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(site);

        var page = context.RequestServices.GetRequiredService<IPublicNotFoundPage>();
        var response = await page.RenderAsync(site, context.RequestAborted).ConfigureAwait(false);

        await WriteAsync(context, response).ConfigureAwait(false);
    }

    /// <summary>Writes a complete public response. HEAD gets the headers only.</summary>
    internal static async Task WriteAsync(HttpContext context, PublicResponse response)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);

        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = response.ContentType;

        foreach (var (name, value) in response.Headers)
        {
            context.Response.Headers[name] = value;
        }

        context.Response.ContentLength = response.Body.Length;

        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(response.Body, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static Task WriteAsync(HttpContext context, string body)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(body, context.RequestAborted);
    }
}
