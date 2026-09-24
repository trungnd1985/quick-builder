namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// Refuses state-changing admin requests that did not come from the admin application
/// (spec FR-018, US2 scenario 8; research R-11).
/// </summary>
/// <remarks>
/// Three layers, none needing a token round-trip:
/// <list type="number">
/// <item>The session cookie is <c>SameSite=Strict</c>, so browsers do not attach it to
/// cross-site requests at all.</item>
/// <item>Every non-GET/HEAD/OPTIONS <c>/api</c> request must carry
/// <c>X-QuickBuilder-Request: 1</c>. A browser cannot add a custom header to a cross-origin
/// request without a CORS preflight, and this application grants none.</item>
/// <item>When the browser says where the request came from — <c>Origin</c> or
/// <c>Sec-Fetch-Site</c> — it must be this application.</item>
/// </list>
/// </remarks>
internal sealed class RequestGuardMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-QuickBuilder-Request";

    private static readonly PathString Api = new("/api");

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        var guarded = request.Path.StartsWithSegments(Api, StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method);

        if (guarded && !CameFromTheApp(request))
        {
            await IdentityProblems.WriteAsync(context, IdentityErrors.RequestRejected).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool CameFromTheApp(HttpRequest request)
    {
        if (!string.Equals(request.Headers[HeaderName], "1", StringComparison.Ordinal))
        {
            return false;
        }

        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0 && fetchSite is not ("same-origin" or "none"))
        {
            return false;
        }

        var origin = request.Headers.Origin.ToString();
        return origin.Length == 0
            || string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    }
}
