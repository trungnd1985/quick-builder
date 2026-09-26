namespace QuickBuilder.Host.Middleware;

/// <summary>
/// The two fixed "not found" answers of the public surface (009 contracts/site-context.md §1).
/// </summary>
/// <remarks>
/// Both are written from constants and never echo anything from the request — no host
/// name, no path — so they confirm nothing a probe could not already see.
/// <list type="bullet">
/// <item><see cref="WriteSiteNotResolvedAsync"/>: no enabled site holds the host name. It is
/// byte-identical for an unknown host and a disabled site (research S-03), and for an
/// installation with zero sites or a hundred (001 FR-014).</item>
/// <item><see cref="WritePublicNotFoundAsync"/>: the site resolved but has no such path. An
/// admin path on a host that is not an admin host name gets exactly this too (S-13), which is
/// what makes the administrative surface invisible on client domains.</item>
/// </list>
/// Having one writer for each is what keeps the cases that must look alike from drifting
/// apart.
/// </remarks>
internal static class PublicResponses
{
    private const string SiteNotResolvedBody =
        """{"type":"https://quickbuilder.dev/errors/site-not-resolved","title":"Not found","status":404,"errorCode":"sites.not_resolved"}""";

    private const string PublicNotFoundBody =
        """{"type":"https://quickbuilder.dev/errors/public-not-found","title":"Not found","status":404,"errorCode":"public.not_found"}""";

    internal static Task WriteSiteNotResolvedAsync(HttpContext context) => WriteAsync(context, SiteNotResolvedBody);

    internal static Task WritePublicNotFoundAsync(HttpContext context) => WriteAsync(context, PublicNotFoundBody);

    private static Task WriteAsync(HttpContext context, string body)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(body, context.RequestAborted);
    }
}
