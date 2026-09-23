namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// The cookies Identity sets, with the attributes spec FR-018 requires (research R-04).
/// </summary>
/// <remarks>
/// HttpOnly so page scripts cannot read them; SameSite=Strict so browsers do not attach them
/// to cross-site requests (research R-11); Secure whenever the request arrived over HTTPS
/// (after forwarded-header processing); scoped to the base path, with no Domain attribute.
/// </remarks>
internal static class IdentityCookies
{
    public const string Session = "qb_session";
    public const string Device = "qb_device";

    public static CookieOptions Options(HttpContext context, TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/",
            IsEssential = true,

            // No MaxAge for the session cookie: a browser-session cookie, with expiry
            // enforced server-side.
            MaxAge = maxAge,
        };
    }
}
