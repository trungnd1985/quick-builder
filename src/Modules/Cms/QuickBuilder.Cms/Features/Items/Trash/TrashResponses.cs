namespace QuickBuilder.Cms.Features.Items.Trash;

/// <summary>What a delete answers: when the content can be restored until (021 FR-002).</summary>
public sealed record TrashReceiptResponse(DateTimeOffset TrashedAtUtc, DateTimeOffset PurgeOnUtc);

/// <summary>Who moved an entry to the trash.</summary>
public sealed record TrashActorResponse(Guid UserId, string DisplayName);

/// <summary>Where an item was: its collection (FR-005).</summary>
public sealed record TrashPlaceResponse(Guid CollectionId, string? CollectionName);

/// <summary>One trash entry (trash-api.yaml TrashEntry).</summary>
public sealed record TrashEntryResponse(
    string Kind,
    Guid Id,
    string Label,
    string? Address,
    TrashPlaceResponse? Place,
    DateTimeOffset TrashedAtUtc,
    TrashActorResponse TrashedBy,
    DateTimeOffset PurgeOnUtc,
    bool WasLive,
    bool HadSchedule,
    bool CanRestore,
    bool CanPurge);

/// <summary>A page of the trash, newest first (FR-010).</summary>
public sealed record TrashListResponse(IReadOnlyList<TrashEntryResponse> Entries, string? NextCursor, int Total);

/// <summary>One entry with its draft, for the read-only preview (FR-011).</summary>
public sealed record TrashEntryDetailResponse(TrashEntryResponse Entry, string? Draft);

/// <summary>
/// What restoring an item will do, shown before confirming (021 TR-10): its slug, a new one when
/// the old is taken (FR-017), and what it misses under the collection's current schema (FR-018).
/// </summary>
public sealed record RestorePlanResponse(
    string Placement,
    Guid CollectionId,
    string Address,
    string? PreviousAddress,
    bool AddressChanged,
    IReadOnlyList<string> SchemaIssues,
    bool WasLive,
    bool HadSchedule,
    bool CanRestoreAndPublish);

/// <summary>A restore request: the slug the member saw, and whether to publish too (TR-11).</summary>
public sealed record RestoreRequest(string? Placement, string? ConfirmedAddress, bool Publish = false);

/// <summary>One restored document.</summary>
public sealed record RestoredResponse(string Kind, Guid Id, string? Address);

/// <summary>What a restore did.</summary>
public sealed record RestoreResultResponse(IReadOnlyList<RestoredResponse> Restored, bool Published);

/// <summary>The outcome of a bulk action, per entry (FR-019).</summary>
public sealed record BulkOutcomeResponse(IReadOnlyList<Guid> Succeeded, IReadOnlyList<BulkFailureResponse> Failed);

/// <summary>Why one entry of a bulk action failed.</summary>
public sealed record BulkFailureResponse(Guid Id, string Code, string Detail);

/// <summary>The trash entry kind of the Cms module.</summary>
internal static class TrashKinds
{
    public const string Item = "item";
}

