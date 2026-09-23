using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure.Security;

namespace QuickBuilder.Identity.Infrastructure.Authentication;

/// <summary>The session behind a valid cookie.</summary>
internal sealed record ValidatedSession(Guid SessionId, Guid UserId);

/// <summary>
/// Server-side sessions (research R-04; spec FR-016, FR-017).
/// </summary>
/// <remarks>
/// Issuing and revoking add to the caller's unit of work and never save, so a session
/// begins or ends in the same transaction as the change that caused it. Validation is the
/// exception: it is a read, plus at most one write a minute to record activity.
/// <para>
/// Nothing is cached. Every request is checked against the database on whichever instance
/// serves it, which is what makes a revocation apply to the very next request everywhere.
/// </para>
/// </remarks>
internal sealed class SessionService(
    IdentityDbContext database,
    IOptions<IdentityOptions> options,
    TimeProvider clock)
{
    private readonly IdentityOptions.SessionSettings _settings = options.Value.Session;

    /// <summary>Starts a session for <paramref name="userId"/> and sets the cookie.</summary>
    public Session Issue(HttpContext context, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var token = Tokens.NewSessionToken();
        var session = Session.Start(
            userId,
            Tokens.Hash(token),
            clock.GetUtcNow(),
            _settings.AbsoluteLifetime,
            context.Connection.RemoteIpAddress);

        database.Sessions.Add(session);
        context.Response.Cookies.Append(IdentityCookies.Session, token, IdentityCookies.Options(context));
        return session;
    }

    /// <summary>
    /// The session behind <paramref name="token"/> when it is unrevoked, unexpired and its
    /// user is active; null otherwise.
    /// </summary>
    public async Task<ValidatedSession?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        var hash = Tokens.Hash(token);
        var found = await database.Sessions
            .Where(s => s.TokenHash == hash)
            .Join(database.Users, s => s.UserId, u => u.Id, (s, u) => new { Session = s, u.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        if (found is null
            || found.Status != UserStatus.Active
            || !found.Session.IsValid(now, _settings.IdleTimeout))
        {
            return null;
        }

        if (found.Session.Touch(now))
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ValidatedSession(found.Session.Id, found.Session.UserId);
    }

    /// <summary>Revokes one session. The caller saves.</summary>
    public async Task RevokeAsync(Guid sessionId, string reason, CancellationToken cancellationToken)
    {
        var session = await database.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);

        session?.Revoke(clock.GetUtcNow(), reason);
    }

    /// <summary>
    /// Revokes every active session of a user, optionally keeping one (the caller's own, for
    /// a password change or "sign out my other sessions"). The caller saves.
    /// </summary>
    /// <returns>How many sessions were revoked.</returns>
    public async Task<int> RevokeAllAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId,
        CancellationToken cancellationToken)
    {
        var sessions = await database.Sessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.Id != exceptSessionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        foreach (var session in sessions)
        {
            session.Revoke(now, reason);
        }

        return sessions.Count;
    }

    public static void ClearCookie(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Delete(IdentityCookies.Session, IdentityCookies.Options(context));
    }
}
