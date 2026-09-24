namespace QuickBuilder.Identity.Domain;

internal static class LinkInvalidationReasons
{
    public const string Replaced = "replaced";
    public const string Deactivated = "deactivated";
    public const string EmailChanged = "email_changed";
}

/// <summary>
/// A single-use, expiring authorisation to set one account's password (spec FR-011,
/// FR-011a; data-model.md, <c>set_password_links</c>).
/// </summary>
/// <remarks>
/// At most one link per account is live at a time; the database enforces that with a
/// partial unique index, so issuing a new link must invalidate the old one first, in the
/// same transaction.
/// </remarks>
internal sealed class SetPasswordLink
{
    private SetPasswordLink()
    {
        TokenHash = [];
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Null when issued by the operator recovery command (research R-16).</summary>
    public Guid? IssuedByUserId { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public DateTimeOffset? InvalidatedAtUtc { get; private set; }

    public string? InvalidationReason { get; private set; }

    public static SetPasswordLink Issue(
        Guid userId,
        byte[] tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        Guid? issuedByUserId)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        return new SetPasswordLink
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            IssuedAtUtc = now,
            ExpiresAtUtc = now + lifetime,
            IssuedByUserId = issuedByUserId,
        };
    }

    /// <summary>Neither used nor invalidated. Expiry is checked separately.</summary>
    public bool IsLive => UsedAtUtc is null && InvalidatedAtUtc is null;

    /// <summary>
    /// Live and unexpired. Whether the account is still active is the caller's check —
    /// a link for a deactivated account is refused like any other (data-model.md).
    /// </summary>
    public bool IsUsable(DateTimeOffset now) => IsLive && now < ExpiresAtUtc;

    public void Consume(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new InvalidOperationException("This link cannot be used.");
        }

        UsedAtUtc = now;
    }

    public void Invalidate(DateTimeOffset now, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!IsLive)
        {
            return;
        }

        InvalidatedAtUtc = now;
        InvalidationReason = reason;
    }
}
