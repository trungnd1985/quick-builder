using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Infrastructure.Security;

namespace QuickBuilder.Identity.Infrastructure.Authentication;

/// <summary>The claims a session principal carries. Grants are deliberately absent.</summary>
internal static class IdentityClaims
{
    public const string UserId = "sub";
    public const string SessionId = "sid";

    public static Guid? GetUserId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(UserId)?.Value, out var id) ? id : null;

    public static Guid? GetSessionId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(SessionId)?.Value, out var id) ? id : null;
}

/// <summary>
/// Authenticates admin requests from the <c>qb_session</c> cookie (research R-04).
/// </summary>
/// <remarks>
/// The principal carries only who the user is and which session this is. What they may do
/// is read per request by <c>AccessEvaluator</c>, never copied into claims, so a permission
/// change applies to the next request without touching the session (FR-017).
/// <para>
/// Challenge and forbid responses are written by <c>AuthorizationResultHandler</c>, so
/// every refusal has the same problem-details shape.
/// </para>
/// </remarks>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(IdentityCookies.Session, out var token) || string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await sessions.ValidateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (session is null)
        {
            // Revoked, expired, unknown, or the user is deactivated. Indistinguishable to the
            // caller by design; the cookie is simply no longer good.
            return AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(IdentityClaims.UserId, session.UserId.ToString()),
                new Claim(IdentityClaims.SessionId, session.SessionId.ToString()),
            ],
            SchemeName,
            IdentityClaims.UserId,
            roleType: null);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
