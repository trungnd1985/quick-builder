using QuickBuilder.Host.Routing;

namespace QuickBuilder.Host.Middleware;

/// <summary>
/// Security headers and asset cache policy for the admin surface (FR-046).
/// </summary>
/// <remarks>
/// Constitution Principle IX: the default configuration must be the secure one. These are
/// applied without an opt-in because an operator who has to enable security headers will
/// not.
/// <para>
/// Cache policy is the other half of FR-046. Vite emits content-hashed asset filenames, so
/// they can be immutable for a year — an upgrade produces new names rather than new
/// content at old names. <c>index.html</c> must never be cached, because it is the file
/// that names which hashed assets to fetch; caching it is exactly how a browser ends up
/// holding a stale mix of old and new.
/// </para>
/// </remarks>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isAdmin = context.Request.Path.StartsWithSegments(
            RoutePrefixes.Admin, StringComparison.OrdinalIgnoreCase);

        if (isAdmin)
        {
            var headers = context.Response.Headers;

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // The admin app is self-contained: no third-party scripts, styles or frames.
            // 'unsafe-inline' for styles is Vue's runtime style injection; scripts get no
            // such exemption.
            headers.ContentSecurityPolicy =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                "connect-src 'self'; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'";

            var path = context.Request.Path.Value ?? string.Empty;
            headers.CacheControl = path.Contains("/assets/", StringComparison.OrdinalIgnoreCase)
                ? "public, max-age=31536000, immutable"
                : "no-store";
        }

        await next(context).ConfigureAwait(false);
    }
}
