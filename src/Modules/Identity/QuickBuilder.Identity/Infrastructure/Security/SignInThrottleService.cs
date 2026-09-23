using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>Whether an attempt may be evaluated now, and if not, when to retry.</summary>
internal sealed record ThrottleDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static ThrottleDecision Allow { get; } = new(true, TimeSpan.Zero);

    public static ThrottleDecision RetryIn(TimeSpan wait) =>
        new(false, wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait);
}

/// <summary>
/// Per-client blocking and per-account progressive delay (spec FR-015, FR-015a; research
/// R-07).
/// </summary>
/// <remarks>
/// State lives in <c>identity.sign_in_throttles</c> and every change is one atomic
/// <c>INSERT … ON CONFLICT … RETURNING</c>, so several instances share the counters without
/// racing. Checks run before any password is hashed, so a throttled attempt costs almost no
/// CPU (SC-013 under attack).
/// <para>
/// Raw SQL through EF Core, parameterised, inside the module that owns the table
/// (constitution Technology Constraints). No table here is site-owned, so there is no site
/// filter to apply.
/// </para>
/// </remarks>
internal sealed class SignInThrottleService(
    IdentityDbContext database,
    IOptions<IdentityOptions> options,
    SecurityEventRecorder events,
    TimeProvider clock)
{
    private readonly IdentityOptions.SignInSettings _settings = options.Value.SignIn;

    /// <summary>
    /// The account key: SHA-256 of the email as it would be matched (trimmed, case-folded).
    /// Computed the same way whether or not an account exists, so throttling reveals
    /// nothing about which accounts exist (FR-015a).
    /// </summary>
    public static string AccountKey(string? email)
    {
        var normalised = User.NormaliseEmail(email).ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    public async Task<ThrottleDecision> CheckClientAsync(string clientKey, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var blockedUntil = await database.SignInThrottles
            .AsNoTracking()
            .Where(t => t.Kind == ThrottleKinds.Client && t.Key == clientKey)
            .Select(t => t.BlockedUntilUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return blockedUntil is { } until && until > now
            ? ThrottleDecision.RetryIn(until - now)
            : ThrottleDecision.Allow;
    }

    public async Task<ThrottleDecision> CheckAccountAsync(string accountKey, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var nextAttempt = await database.SignInThrottles
            .AsNoTracking()
            .Where(t => t.Kind == ThrottleKinds.Account && t.Key == accountKey)
            .Select(t => t.NextAttemptAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return nextAttempt is { } next && next > now
            ? ThrottleDecision.RetryIn(next - now)
            : ThrottleDecision.Allow;
    }

    /// <summary>
    /// Counts a failed attempt against the client and, when given, the account. When this
    /// failure blocks the client, a <c>client_blocked</c> event is added to the unit of work;
    /// the caller saves it with its own <c>sign_in</c> event.
    /// </summary>
    /// <returns>True when this failure started a client block.</returns>
    public async Task<bool> RecordFailureAsync(string clientKey, string? accountKey, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var blockUntil = now + _settings.ClientBlockDuration;
        var threshold = _settings.ClientFailureThreshold;

        // A block that has expired starts the count again; an active block is left alone.
        var rows = await database.Database
            .SqlQuery<ClientRow>($"""
                INSERT INTO identity.sign_in_throttles AS t
                    (kind, key, consecutive_failures, blocked_until_utc, next_attempt_at_utc, updated_at_utc)
                VALUES ('client', {clientKey}, 1, CASE WHEN 1 >= {threshold} THEN {blockUntil} END, NULL, {now})
                ON CONFLICT (kind, key) DO UPDATE SET
                    consecutive_failures = CASE
                        WHEN t.blocked_until_utc IS NOT NULL AND t.blocked_until_utc <= {now} THEN 1
                        ELSE t.consecutive_failures + 1 END,
                    blocked_until_utc = CASE
                        WHEN t.blocked_until_utc IS NOT NULL AND t.blocked_until_utc > {now} THEN t.blocked_until_utc
                        WHEN (CASE
                                WHEN t.blocked_until_utc IS NOT NULL AND t.blocked_until_utc <= {now} THEN 1
                                ELSE t.consecutive_failures + 1 END) >= {threshold} THEN {blockUntil}
                        ELSE NULL END,
                    updated_at_utc = {now}
                RETURNING t.blocked_until_utc AS "BlockedUntilUtc"
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (accountKey is not null)
        {
            await RecordAccountFailureAsync(accountKey, now, cancellationToken).ConfigureAwait(false);
        }

        var newlyBlocked = rows.Count == 1 && rows[0].BlockedUntilUtc == blockUntil;
        if (newlyBlocked)
        {
            events.Record(
                SecurityEventTypes.ClientBlocked,
                SecurityEventOutcomes.Refused,
                actorUserId: null,
                subjectUserId: null,
                new ClientBlockedDetails(blockUntil));
        }

        return newlyBlocked;
    }

    /// <summary>A successful sign-in clears the account's wait and the client's count.</summary>
    public async Task RecordSuccessAsync(string clientKey, string accountKey, CancellationToken cancellationToken)
    {
        await database.Database
            .ExecuteSqlAsync(
                $"""
                DELETE FROM identity.sign_in_throttles
                WHERE (kind = 'account' AND key = {accountKey})
                   OR (kind = 'client' AND key = {clientKey} AND blocked_until_utc IS NULL)
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RecordAccountFailureAsync(string accountKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var threshold = _settings.AccountFailureThreshold;
        var maxSeconds = _settings.AccountMaxDelay.TotalSeconds;

        // From the threshold-th consecutive failure the wait doubles — 1 s, 2 s, 4 s … —
        // capped at AccountMaxDelay. The exponent is capped too, so a long attack cannot
        // overflow the arithmetic.
        await database.Database
            .ExecuteSqlAsync(
                $"""
                INSERT INTO identity.sign_in_throttles AS t
                    (kind, key, consecutive_failures, blocked_until_utc, next_attempt_at_utc, updated_at_utc)
                VALUES ('account', {accountKey}, 1, NULL,
                        CASE WHEN 1 >= {threshold}
                             THEN {now} + make_interval(secs => LEAST(1.0, {maxSeconds})) END,
                        {now})
                ON CONFLICT (kind, key) DO UPDATE SET
                    consecutive_failures = t.consecutive_failures + 1,
                    next_attempt_at_utc = CASE
                        WHEN t.consecutive_failures + 1 >= {threshold}
                        THEN {now} + make_interval(secs => LEAST(
                            power(2, LEAST(t.consecutive_failures + 1 - {threshold}, 30)),
                            {maxSeconds}))
                        ELSE NULL END,
                    updated_at_utc = {now}
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record ClientRow(DateTimeOffset? BlockedUntilUtc);
}
