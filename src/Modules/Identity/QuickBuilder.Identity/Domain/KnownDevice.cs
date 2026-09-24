namespace QuickBuilder.Identity.Domain;

/// <summary>
/// A browser that has signed in successfully to one account (spec FR-015a; research R-07).
/// </summary>
/// <remarks>
/// A correct password from a known device skips the per-account wait, so an attacker who
/// times failures to each wait's expiry cannot keep the owner out. It never skips the
/// per-client block. Repeated failures from the device revoke it, so a stolen device
/// cookie cannot be used to grind passwords.
/// </remarks>
internal sealed class KnownDevice
{
    private KnownDevice()
    {
        TokenHash = [];
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastUsedAtUtc { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static KnownDevice Remember(Guid userId, byte[] tokenHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        return new KnownDevice
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            LastUsedAtUtc = now,
        };
    }

    public bool IsValid(DateTimeOffset now, TimeSpan lifetime) =>
        RevokedAtUtc is null && now < LastUsedAtUtc + lifetime;

    /// <summary>Counts a failure. Returns true when this failure revoked the device.</summary>
    public bool RecordFailure(int threshold, DateTimeOffset now)
    {
        if (RevokedAtUtc is not null)
        {
            return false;
        }

        ConsecutiveFailures++;
        if (ConsecutiveFailures < threshold)
        {
            return false;
        }

        RevokedAtUtc = now;
        return true;
    }

    public void RecordSuccess(DateTimeOffset now)
    {
        ConsecutiveFailures = 0;
        LastUsedAtUtc = now;
    }

    public void Revoke(DateTimeOffset now) => RevokedAtUtc ??= now;
}
