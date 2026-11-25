using Microsoft.AspNetCore.Routing;
using QuickBuilder.Shared.Kernel.Routing;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// Keeps the page route off the administrative surface (012 research PR-08).
/// </summary>
/// <remarks>
/// The page route is a catch-all. Without this it would compete with the admin shell's
/// deep-link fallback and answer unknown API paths. <c>/api</c>, <c>/admin</c> and
/// <c>/health</c> are the host's administrative prefixes (<c>RoutePrefixes</c>); this module
/// cannot reference the host, so they are named here, and <c>PublicRouteBoundaryTests</c>
/// proves the two agree.
/// </remarks>
internal sealed class PublicPathConstraint : IRouteConstraint
{
    public const string Name = "publicPath";

    /// <remarks>
    /// <c>_preview</c> is not administrative, but no page may answer under it either (022
    /// research PV-05). The slug grammar already refuses a leading <c>_</c>; this is defence in
    /// depth.
    /// </remarks>
    internal static readonly string[] AdministrativeSegments =
        [ApiRoutes.Segment, AdminRoutes.Segment, HealthRoutes.Segment, PublicRoutes.PreviewSegment];

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!values.TryGetValue(routeKey, out var value) || value is not string path || path.Length == 0)
        {
            return true;
        }

        var first = path.TrimStart('/').Split('/', 2)[0];
        return !AdministrativeSegments.Contains(first, StringComparer.OrdinalIgnoreCase);
    }
}
