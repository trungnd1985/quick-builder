using System.Net;

namespace QuickBuilder.Identity.Domain;

/// <summary>Why a session ended early (data-model.md, <c>sessions.revocation_reason</c>).</summary>
internal static class SessionRevocationReasons
{
    public const string SignOut = "sign_out";
    public const string SignOutEverywhere = "sign_out_everywhere";
    public const string SignOutOthers = "sign_out_others";
    public const string PasswordChanged = "password_changed";
    public const string PasswordSet = "password_set";
    public const string EmailChanged = "email_changed";
    public const string Deactivated = "deactivated";
}

/// <summary>
/// One authenticated sign-in (research R-04). The browser holds only an opaque token; this
/// row holds its SHA-256.
/// </summary>
/// <remarks>
/// Expiry is derived, never written: a session is valid while unrevoked, before its fixed
/// absolute expiry, and within the idle timeout of its last activity. The absolute expiry
/// is set at creation, so a backwards clock jump can extend a session by at most the size
/// of the jump (spec edge case "Clock changes").
/// </remarks>
internal sealed class Session
{
    /// <summary>
    /// <see cref="LastSeenAtUtc"/> is written at most this often, to avoid a database write
    /// on every request. Idle expiry therefore has this granularity (research R-04).
    /// </summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private Session()
    {
        TokenHash = [];
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? RevocationReason { get; private set; }

    public IPAddress? ClientAddress { get; private set; }

    public static Session Start(
        Guid userId,
        byte[] tokenHash,
        DateTimeOffset now,
        TimeSpan absoluteLifetime,
        IPAddress? clientAddress)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        return new Session
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            AbsoluteExpiresAtUtc = now + absoluteLifetime,
            ClientAddress = clientAddress,
        };
    }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsValid(DateTimeOffset now, TimeSpan idleTimeout) =>
        !IsRevoked
        && now < AbsoluteExpiresAtUtc
        && now < LastSeenAtUtc + idleTimeout;

    /// <summary>Records activity. Returns true when the row changed and needs saving.</summary>
    public bool Touch(DateTimeOffset now)
    {
        if (now - LastSeenAtUtc < TouchInterval)
        {
            return false;
        }

        LastSeenAtUtc = now;
        return true;
    }

    public void Revoke(DateTimeOffset now, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = now;
        RevocationReason = reason;
    }
}
