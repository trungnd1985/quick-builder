namespace QuickBuilder.Identity.Domain;

internal static class ThrottleKinds
{
    /// <summary>Keyed by client address (IPv6 aggregated to /64).</summary>
    public const string Client = "client";

    /// <summary>Keyed by SHA-256 of the normalised email, whether or not the account exists.</summary>
    public const string Account = "account";
}

/// <summary>
/// Sign-in throttling state for one client or one account (research R-07; data-model.md,
/// <c>sign_in_throttles</c>).
/// </summary>
/// <remarks>
/// Changed only through a single atomic upsert in <c>SignInThrottleService</c>, so
/// instances never race each other. This type exists to map the table and to be read.
/// </remarks>
internal sealed class SignInThrottle
{
    private SignInThrottle()
    {
        Kind = string.Empty;
        Key = string.Empty;
    }

    public string Kind { get; private set; }

    public string Key { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    /// <summary>Client throttles only.</summary>
    public DateTimeOffset? BlockedUntilUtc { get; private set; }

    /// <summary>Account throttles only: the earliest time an attempt will be evaluated.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }
}
