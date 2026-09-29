using Microsoft.Extensions.Options;
using QuickBuilder.Host.Configuration;
using QuickBuilder.Host.Routing;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Host.Middleware;

/// <summary>
/// Keeps the administrative surface off host names that are not admin host names
/// (009 FR-018a, research S-13).
/// </summary>
/// <remarks>
/// Does nothing unless <c>Hosting:AdminHostNames</c> is set. When it is, a request for
/// <c>/api</c> or <c>/admin</c> on any other host is answered here as a public request
/// would be: the unknown-site response when no enabled site holds the host, otherwise
/// that site's public not-found — the very bytes a missing page gets.
/// <para>
/// <b>Why here, this early.</b> It runs before the security headers, which mark admin
/// paths with their own headers, and before static files, which would otherwise serve
/// the admin shell's scripts on any host. Either would give the surface away. It also runs
/// before authentication, so a session cookie sent to a client domain is never even read.
/// </para>
/// <para>
/// <c>/health</c> answers everywhere: orchestrators and the compose health check call it on
/// the container's own address, and it says nothing about the administrative surface.
/// </para>
/// </remarks>
internal sealed class AdminHostConfinementMiddleware(RequestDelegate next, IOptions<HostingOptions> hosting)
{
    private readonly IReadOnlySet<string> _adminHostNames = hosting.Value.NormalisedAdminHostNames();

    public async Task InvokeAsync(HttpContext context, ISiteDirectory siteDirectory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(siteDirectory);

        if (_adminHostNames.Count == 0 || !IsAdministrativePath(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (HostNames.TryNormalise(context.Request.Host.Host, out var host) && _adminHostNames.Contains(host))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Answered as the public surface would answer this path on this host. Resolving
        // first keeps the two not-found answers apart exactly as they are for real public
        // paths, so neither reveals anything the other does not.
        var site = await siteDirectory
            .FindByHostNameAsync(context.Request.Host.Host, context.RequestAborted)
            .ConfigureAwait(false);

        if (site is null)
        {
            await PublicResponses.WriteSiteNotResolvedAsync(context).ConfigureAwait(false);
            return;
        }

        // The not-found page reads the site's published design and home page, which are
        // filtered by the resolved site, so resolve it here as the public surface would.
        context.RequestServices.GetRequiredService<ISiteContextSetter>().SetSite(site.Id);
        await PublicResponses.WritePublicNotFoundAsync(context, site).ConfigureAwait(false);
    }

    private static bool IsAdministrativePath(PathString path) =>
        path.StartsWithSegments(RoutePrefixes.Api, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(RoutePrefixes.Admin, StringComparison.OrdinalIgnoreCase);
}
