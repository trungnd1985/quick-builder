namespace QuickBuilder.Pages.Features.Trash;

/// <summary>What a delete answers: when the content can be restored until (021 FR-002).</summary>
public sealed record TrashReceiptResponse(DateTimeOffset TrashedAtUtc, DateTimeOffset PurgeOnUtc);

/// <summary>Who moved an entry to the trash.</summary>
public sealed record TrashActorResponse(Guid UserId, string DisplayName);

/// <summary>Where an entry was (FR-005). <c>parentState</c> is live, trashed, gone or none.</summary>
public sealed record TrashPlaceResponse(Guid? ParentId, string? ParentLabel, string ParentState);

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

/// <summary>Where a restore puts the page (trash-api.yaml RestorePlan.target).</summary>
public sealed record RestoreTargetResponse(Guid? ParentId, string Label, int Position);

/// <summary>A trashed ancestor that "with parents" would restore too.</summary>
public sealed record RestoreParentResponse(Guid Id, string Label, string Address);

/// <summary>What a restore will do, shown before confirming (021 TR-10).</summary>
public sealed record RestorePlanResponse(
    string Placement,
    IReadOnlyList<string> PlacementOptions,
    RestoreTargetResponse Target,
    IReadOnlyList<RestoreParentResponse> ParentsToRestore,
    string Address,
    string? PreviousAddress,
    bool AddressChanged,
    bool BecomesHome,
    bool TemplateDropped,
    bool WasLive,
    bool HadSchedule,
    bool CanRestoreAndPublish);

/// <summary>A restore request. <c>Placement</c> is original, with_parents, chosen or top.</summary>
public sealed record RestoreRequest(string? Placement, Guid? ParentId, string? ConfirmedAddress, bool Publish = false);

/// <summary>One restored document.</summary>
public sealed record RestoredResponse(string Kind, Guid Id, string? Address);

/// <summary>What a restore did.</summary>
public sealed record RestoreResultResponse(IReadOnlyList<RestoredResponse> Restored, bool Published);

/// <summary>The outcome of a bulk action, per entry (FR-019).</summary>
public sealed record BulkOutcomeResponse(IReadOnlyList<Guid> Succeeded, IReadOnlyList<BulkFailureResponse> Failed);

/// <summary>Why one entry of a bulk action failed.</summary>
public sealed record BulkFailureResponse(Guid Id, string Code, string Detail);

/// <summary>Trash entry kinds of the Pages module.</summary>
internal static class TrashKinds
{
    public const string Page = "page";

    public const string Component = "component";
}

/// <summary>Restore placements (021 TR-10).</summary>
internal static class RestorePlacements
{
    public const string Original = "original";

    public const string WithParents = "with_parents";

    public const string Chosen = "chosen";

    public const string Top = "top";
}
