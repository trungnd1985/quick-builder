namespace QuickBuilder.Editor.Features.EditingSessions;

/// <summary>A member as presence shows them: the display name only (023 FR-013).</summary>
public sealed record EditingMemberResponse(Guid Id, string DisplayName);

/// <summary>Who holds a document's lock, and since when (023 contracts/editing-api.yaml, <c>Presence.lock</c>).</summary>
/// <param name="InactiveSeconds">Seconds since the holder's last change or editing action.</param>
/// <param name="IsYou">This session holds it.</param>
/// <param name="IsYourOtherSession">Another session of the same member holds it (FR-014).</param>
public sealed record EditingLockResponse(
    Guid HolderSessionId,
    EditingMemberResponse Holder,
    DateTimeOffset Since,
    bool Stale,
    int InactiveSeconds,
    bool IsYou,
    bool IsYourOtherSession);

/// <summary>Set after this session lost the lock to someone else: who, when, and why (FR-018).</summary>
public sealed record LostLockResponse(DateTimeOffset At, EditingMemberResponse By, string Reason);

/// <summary>Another live session on the same document.</summary>
public sealed record EditingOtherResponse(EditingMemberResponse Member, DateTimeOffset Since, bool HoldsLock);

/// <summary>
/// Everything one session needs to show (023 contracts/editing-api.yaml, <c>Presence</c>). All
/// times are the server's (research CE-05).
/// </summary>
/// <param name="Role"><c>viewer</c> or <c>holder</c>.</param>
/// <param name="Access"><c>edit</c>, or <c>view-only</c> without the kind's edit capability.</param>
public sealed record PresenceResponse(
    Guid SessionId,
    DateTimeOffset ServerNow,
    string Role,
    string Access,
    EditingLockResponse? Lock,
    LostLockResponse? LostLock,
    IReadOnlyList<EditingOtherResponse> Others);

/// <summary>A document of the listed kind with a live lock holder, for markers in lists (FR-012).</summary>
public sealed record HolderResponse(Guid DocumentId, EditingMemberResponse Holder, DateTimeOffset Since, bool Stale);

public sealed record HoldersResponse(DateTimeOffset ServerNow, IReadOnlyList<HolderResponse> Holders);

/// <summary>Opening a session: which document.</summary>
public sealed record OpenSessionRequest(string? Kind, Guid DocumentId, Guid? CollectionId);

/// <summary>A heartbeat: whether the holder changed anything since the previous one (CE-05).</summary>
public sealed record HeartbeatRequest(bool Active);

/// <summary>A takeover or a move names the holder the member saw, so a changed holder fails cleanly.</summary>
public sealed record HolderSessionRequest(Guid HolderSessionId);
