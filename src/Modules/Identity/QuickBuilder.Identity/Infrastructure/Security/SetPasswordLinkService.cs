using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>A link as the administrator sees it, once: the token and when it stops working.</summary>
internal sealed record IssuedLink(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// One-time set-password links (spec FR-011, FR-011a; research R-10).
/// </summary>
/// <remarks>
/// Only the SHA-256 of a token is stored. At most one link per account is live: issuing a
/// new one invalidates the old one first. Every method adds to the caller's unit of work;
/// the invalidation is a set-based update that runs immediately, so callers wrap
/// <see cref="IssueAsync"/> in a transaction.
/// </remarks>
internal sealed class SetPasswordLinkService(
    IdentityDbContext database,
    IOptions<IdentityOptions> options,
    SessionService sessions,
    KnownDeviceService devices,
    SecurityEventRecorder events,
    Pbkdf2PasswordHasher hasher,
    TimeProvider clock)
{
    /// <param name="issuedByUserId">The administrator; null for the operator recovery command.</param>
    public async Task<IssuedLink> IssueAsync(Guid userId, Guid? issuedByUserId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // The partial unique index allows one live link per account, so the old one must be
        // retired before the new one is inserted, not in the same batch.
        await InvalidateLiveAsync(userId, LinkInvalidationReasons.Replaced, cancellationToken).ConfigureAwait(false);

        var token = Tokens.NewLinkToken();
        var link = SetPasswordLink.Issue(userId, Tokens.Hash(token), now, options.Value.SetPasswordLinks.Lifetime, issuedByUserId);
        database.SetPasswordLinks.Add(link);

        events.Record(
            SecurityEventTypes.SetPasswordLinkIssued,
            SecurityEventOutcomes.Succeeded,
            issuedByUserId,
            userId,
            new LinkIssuedDetails(link.ExpiresAtUtc, issuedByUserId is null ? "operator" : "administrator"));

        return new IssuedLink(token, link.ExpiresAtUtc);
    }

    /// <summary>Retires the account's live link, if any (deactivation, email change, replacement).</summary>
    public Task InvalidateLiveAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return database.SetPasswordLinks
            .Where(l => l.UserId == userId && l.UsedAtUtc == null && l.InvalidatedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(l => l.InvalidatedAtUtc, (DateTimeOffset?)now)
                    .SetProperty(l => l.InvalidationReason, reason),
                cancellationToken);
    }

    /// <summary>
    /// When the link will expire, or null when it cannot be used. Opening an unusable link
    /// is recorded as a refusal, like using one (FR-029). The caller saves.
    /// </summary>
    public async Task<DateTimeOffset?> CheckAsync(string? token, CancellationToken cancellationToken)
    {
        var found = await FindUsableAsync(token, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            RecordRefusal();
        }

        return found?.Link.ExpiresAtUtc;
    }

    /// <summary>
    /// Uses the link: sets the password, spends the link, and ends every session and known
    /// device of the account (FR-011a). The caller saves.
    /// </summary>
    public async Task<Result> ConsumeAsync(string? token, string? newPassword, CancellationToken cancellationToken)
    {
        var found = await FindUsableAsync(token, cancellationToken).ConfigureAwait(false);
        if (found is not var (link, user))
        {
            RecordRefusal();
            return Result.Failure(IdentityErrors.LinkInvalid);
        }

        // Checked only for a usable link, so a weak password never tells a guesser that the
        // token was real, and never spends the link.
        var weakness = PasswordPolicy.Check(newPassword, user.Email, user.DisplayName, "NewPassword");
        if (weakness.Count > 0)
        {
            throw new RequestValidationException(ValidationResult.Invalid(weakness));
        }

        var now = clock.GetUtcNow();
        user.SetPasswordHash(await hasher.HashAsync(newPassword!, cancellationToken).ConfigureAwait(false), now);
        link.Consume(now);

        await sessions.RevokeAllAsync(user.Id, SessionRevocationReasons.PasswordSet, exceptSessionId: null, cancellationToken).ConfigureAwait(false);
        await devices.RevokeAllAsync(user.Id, actorUserId: user.Id, "password_set", cancellationToken).ConfigureAwait(false);

        events.Record(SecurityEventTypes.SetPasswordLinkUsed, SecurityEventOutcomes.Succeeded, user.Id, user.Id);
        return Result.Success();
    }

    /// <summary>
    /// The account is never named: an unusable link reveals nothing about it, not even in
    /// the event (FR-011a).
    /// </summary>
    private void RecordRefusal() =>
        events.Record(
            SecurityEventTypes.SetPasswordLinkUsed,
            SecurityEventOutcomes.Refused,
            actorUserId: null,
            subjectUserId: null,
            new ReasonDetails("link_invalid"));

    private async Task<(SetPasswordLink Link, User User)?> FindUsableAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = Tokens.Hash(token);
        var found = await database.SetPasswordLinks
            .Where(l => l.TokenHash == hash)
            .Join(database.Users, l => l.UserId, u => u.Id, (l, u) => new { Link = l, User = u })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found is not null && found.Link.IsUsable(clock.GetUtcNow()) && found.User.IsActive
            ? (found.Link, found.User)
            : null;
    }
}
