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
/// (FR-011b, FR-014a). Admin paths on hosts that are not admin host names never get here:
/// <see cref="AdminHostConfinementMiddleware"/> answers them first (009 FR-018a).
/// <para>
/// Because it runs before any handler, handlers never read the Host header themselves —
/// they read <see cref="ISiteContext"/>, or <see cref="ICurrentSite"/> for the site's
/// details. That is the whole point: one place decides which site a request belongs to.
/// </para>
/// <para>
/// Only enabled sites resolve (009 research S-03). A disabled site's host names get the
/// same answer as unknown ones, from the same query, so nothing tells them apart.
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
        var site = await siteDirectory
            .FindByHostNameAsync(context.Request.Host.Host, context.RequestAborted)
            .ConfigureAwait(false);

        if (site is null)
        {
            // FR-014, FR-032: disclose nothing about which sites exist, and do not echo or
            // log the host name — that would confirm what was probed.
            logger.LogInformation("No enabled site is configured for the requested host name.");
            await PublicResponses.WriteSiteNotResolvedAsync(context).ConfigureAwait(false);
            return;
        }

        siteContextSetter.SetSite(site.Id);

        // A public path nothing handles gets the one public not-found, the same answer an
        // admin path on a client domain gets (research S-13). Paths under /_media stay with
        // Media, which answers its own misses.
        if (context.GetEndpoint() is null && !IsPublicReserved(context.Request.Path))
        {
            await PublicResponses.WritePublicNotFoundAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsPublicReserved(PathString path) =>
        RoutePrefixes.PublicReservedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
