namespace QuickBuilder.Editor.Domain;

/// <summary>
/// When an editing session is gone, when a lock is stale, and who may claim, take over or
/// move a lock (023 data-model §1.1, research CE-03 to CE-06).
/// </summary>
/// <remarks>
/// Every comparison uses the server's clock, so "since" and "inactive for" are the same for
/// every viewer whatever their devices say (spec edge case "Clock differences"). The periods
/// are constants, not settings (CE-05); the spec allows making them settings later.
/// </remarks>
internal static class EditingRules
{
    /// <summary>A session that gave no sign of life for this long is gone (FR-016).</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(2);

    /// <summary>A lock whose holder made no change for this long is stale (FR-017).</summary>
    public static readonly TimeSpan StalePeriod = TimeSpan.FromMinutes(30);

    /// <summary>Takeover records older than this are deleted (CE-12).</summary>
    public static readonly TimeSpan TakeoverRetention = TimeSpan.FromDays(90);

    /// <summary>The live holder of a document's lock, as the rules need to see it.</summary>
    public sealed record Holder(Guid SessionId, Guid UserId, DateTimeOffset LastSeenAt, DateTimeOffset LastActivityAt);

    public static bool IsGone(DateTimeOffset lastSeenAt, DateTimeOffset now) => now - lastSeenAt >= Expiry;

    public static bool IsStale(DateTimeOffset lastActivityAt, DateTimeOffset now) => now - lastActivityAt >= StalePeriod;

    /// <summary>
    /// A claim succeeds when nobody holds the lock, or only a gone session does. A stale holder
    /// still holds it: only a takeover moves it (FR-009, FR-017).
    /// </summary>
    public static bool MayClaim(Holder? holder, DateTimeOffset now) => holder is null || IsGone(holder.LastSeenAt, now);

    /// <summary>
    /// A takeover needs the holder the member saw to still be the holder, to be another member,
    /// alive and stale (FR-017). A gone holder is claimed, not taken over.
    /// </summary>
    public static bool CanTakeOver(Holder? holder, Guid expectedHolderSessionId, Guid requesterUserId, DateTimeOffset now) =>
        holder is not null
        && holder.SessionId == expectedHolderSessionId
        && holder.UserId != requesterUserId
        && !IsGone(holder.LastSeenAt, now)
        && IsStale(holder.LastActivityAt, now);

    /// <summary>"Edit here instead": the holder is another session of the same member, active or stale (FR-014).</summary>
    public static bool CanMove(Holder? holder, Guid expectedHolderSessionId, Guid requesterUserId, DateTimeOffset now) =>
        holder is not null
        && holder.SessionId == expectedHolderSessionId
        && holder.UserId == requesterUserId
        && !IsGone(holder.LastSeenAt, now);
}
