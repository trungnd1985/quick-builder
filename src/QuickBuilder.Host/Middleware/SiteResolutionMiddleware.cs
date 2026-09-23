using QuickBuilder.Host.Routing;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Host.Middleware;

/// <summary>
/// Resolves the site for public-surface requests from the Host header (FR-011a, FR-014).
/// </summary>
/// <remarks>
/// Runs only on the public surface. Administrative, health and asset requests skip it
/// entirely, which is what lets the admin shell work on an installation with zero sites
/// (FR-011b, FR-014a).
/// <para>
/// Because it runs before any handler, handlers never read the Host header themselves —
/// they read <see cref="ISiteContext"/>. That is the whole point: one place decides which
/// site a request belongs to.
/// </para>
/// </remarks>
internal sealed class SiteResolutionMiddleware(RequestDelegate next, ILogger<SiteResolutionMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ISiteContextSetter siteContextSetter,
        ISiteDirectory siteDirectory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(siteContextSetter);
        ArgumentNullException.ThrowIfNull(siteDirectory);

        if (RoutePrefixes.IsReserved(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Host.Host excludes the port: ports are the proxy's concern (D-12) and a site is
        // identified by name, not by where it happens to be listening.
        var hostName = context.Request.Host.Host;
        var site = await siteDirectory
            .FindByHostNameAsync(hostName, context.RequestAborted)
            .ConfigureAwait(false);

        if (site is null)
        {
            // FR-014: the response must disclose nothing about which sites exist, so it is
            // byte-identical whether this installation has zero sites or a hundred. No
            // host name is echoed back either — that would confirm what was probed.
            logger.LogInformation("No site is configured for the requested host name.");

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/problem+json";
            await context.Response
                .WriteAsync(
                    """
                    {"type":"https://quickbuilder.dev/errors/site-not-resolved","title":"Not found","status":404,"errorCode":"sites.not_resolved"}
                    """,
                    context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        siteContextSetter.SetSite(site.Id);
        await next(context).ConfigureAwait(false);
    }
}
