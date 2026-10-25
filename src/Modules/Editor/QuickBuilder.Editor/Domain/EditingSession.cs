using QuickBuilder.Editor.Contracts;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Editor.Domain;

/// <summary>
/// One open editor or item form — a browser tab — on one page, component or item (023
/// data-model §1.1, research CE-01).
/// </summary>
/// <remarks>
/// <para>
/// Opening makes a viewer, who blocks nobody. The first change takes the lock (clarification
/// Q3). At most one session of a document holds it; the partial unique index on
/// <c>holds_lock</c> is what guarantees that across instances (FR-010, CE-02), so this type
/// only keeps its own fields consistent.
/// </para>
/// <para>
/// Expiry and staleness are never stored: they follow from <see cref="LastSeenAt"/> and
/// <see cref="LastActivityAt"/> and the server's clock (<see cref="EditingRules"/>), so they
/// are right even when no cleanup has run.
/// </para>
/// </remarks>
internal sealed class EditingSession : ISiteOwned
{
    /// <summary>Why a session lost the lock to someone else.</summary>
    public static class LostReasons
    {
        public const string Takeover = "takeover";

        public const string Moved = "moved";
    }

    private EditingSession(Guid siteId, string kind, Guid documentId, Guid? collectionId, Guid userId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        SiteId = siteId;
        DocumentKind = kind;
        DocumentId = documentId;
        CollectionId = collectionId;
        UserId = userId;
        OpenedAt = now;
        LastSeenAt = now;
    }

    // EF Core materialisation.
    private EditingSession()
    {
        DocumentKind = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>One of <see cref="EditingDocumentKinds"/>.</summary>
    public string DocumentKind { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>For items: their collection, so a deleted collection ends the session (CE-13).</summary>
    public Guid? CollectionId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public bool HoldsLock { get; private set; }

    /// <summary>Since when the lock is held: "editing since". Set only while holding.</summary>
    public DateTimeOffset? LockTakenAt { get; private set; }

    /// <summary>The last change or editing action while holding (CE-05). Set only while holding.</summary>
    public DateTimeOffset? LastActivityAt { get; private set; }

    public DateTimeOffset? LostLockAt { get; private set; }

    public Guid? LostLockToUserId { get; private set; }

    /// <summary>One of <see cref="LostReasons"/>, after this session lost the lock.</summary>
    public string? LostLockReason { get; private set; }

    public static EditingSession Open(Guid siteId, string kind, Guid documentId, Guid? collectionId, Guid userId, DateTimeOffset now)
    {
        if (!EditingDocumentKinds.IsKnown(kind))
        {
            throw new ArgumentException($"Unknown document kind '{kind}'.", nameof(kind));
        }

        if (kind == EditingDocumentKinds.Item && collectionId is null)
        {
            throw new ArgumentException("An item session names its collection.", nameof(collectionId));
        }

        return new EditingSession(siteId, kind, documentId, kind == EditingDocumentKinds.Item ? collectionId : null, userId, now);
    }

    public bool IsGone(DateTimeOffset now) => EditingRules.IsGone(LastSeenAt, now);

    public bool IsStale(DateTimeOffset now) => HoldsLock && LastActivityAt is { } activity && EditingRules.IsStale(activity, now);

    /// <summary>A heartbeat. Activity counts only while holding the lock (spec edge case: reading is not activity).</summary>
    public void Seen(bool active, DateTimeOffset now)
    {
        LastSeenAt = now;
        if (active && HoldsLock)
        {
            LastActivityAt = now;
        }
    }

    /// <summary>Takes the lock. The caller has checked <see cref="EditingRules"/>; the index arbitrates races.</summary>
    public void TakeLock(DateTimeOffset now)
    {
        if (HoldsLock)
        {
            return;
        }

        HoldsLock = true;
        LockTakenAt = now;
        LastActivityAt = now;
        LastSeenAt = now;
        LostLockAt = null;
        LostLockToUserId = null;
        LostLockReason = null;
    }

    /// <summary>Gives the lock up to someone else, remembering who and why for this session's next heartbeat.</summary>
    public void LoseLock(Guid toUserId, string reason, DateTimeOffset now)
    {
        if (reason is not (LostReasons.Takeover or LostReasons.Moved))
        {
            throw new ArgumentException($"Unknown reason '{reason}'.", nameof(reason));
        }

        ReleaseLock();
        LostLockAt = now;
        LostLockToUserId = toUserId;
        LostLockReason = reason;
    }

    /// <summary>Stops holding without anyone taking over: lost permission (FR-019).</summary>
    public void ReleaseLock()
    {
        HoldsLock = false;
        LockTakenAt = null;
        LastActivityAt = null;
    }

    /// <summary>The holder as <see cref="EditingRules"/> sees it; null unless this session holds the lock.</summary>
    public EditingRules.Holder? AsHolder() =>
        HoldsLock && LastActivityAt is { } activity ? new EditingRules.Holder(Id, UserId, LastSeenAt, activity) : null;
}
