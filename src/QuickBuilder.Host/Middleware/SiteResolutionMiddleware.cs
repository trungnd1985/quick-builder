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

        // 022 research PV-05: previews name their site in the link or route, and answer on
        // every host name, the admin host names included.
        if (RoutePrefixes.IsReserved(context.Request.Path) || RoutePrefixes.IsUnresolvedPublic(context.Request.Path))
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

        // 017 FR-014, research SE-07: one host name serves the site when the Administrator
        // chose so. Media files and the block stylesheet keep answering on every host name, so
        // images in old emails and embeds still load.
        if (site.RedirectOtherHostNames && !IsPrimaryHost(context.Request.Host.Host, site) && !ServedOnEveryHost(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = site.BaseUrl.TrimEnd('/') + context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent();
            context.Response.Headers.CacheControl = "public, max-age=300";
            return;
        }

        // A public path nothing handles gets the one public not-found, the same answer an
        // admin path on a client domain gets (research S-13). Paths under /_media stay with
        // Media, which answers its own misses.
        if (context.GetEndpoint() is null && !IsPublicReserved(context.Request.Path))
        {
            await PublicResponses.WritePublicNotFoundAsync(context, site).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>Whether the request's host name is the site's primary one, compared as the directory stores names.</summary>
    private static bool IsPrimaryHost(string host, SiteDto site)
    {
        var name = host.TrimEnd('.');
        try
        {
            name = new System.Globalization.IdnMapping().GetAscii(name);
        }
        catch (ArgumentException)
        {
            // Not a valid IDN; it still resolved, so compare it as written.
        }

        return string.Equals(name, site.PrimaryHostName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, site.PrimaryHostName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ServedOnEveryHost(PathString path) =>
        path.StartsWithSegments("/_media", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/_assets", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublicReserved(PathString path) =>
        RoutePrefixes.PublicReservedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
