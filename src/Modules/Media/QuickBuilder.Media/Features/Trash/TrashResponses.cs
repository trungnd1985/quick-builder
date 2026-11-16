namespace QuickBuilder.Media.Features.Trash;

/// <summary>What a delete answers: when the file can be restored until (021 FR-002).</summary>
public sealed record TrashReceiptResponse(DateTimeOffset TrashedAtUtc, DateTimeOffset PurgeOnUtc);

/// <summary>Who moved an entry to the trash.</summary>
public sealed record TrashActorResponse(Guid UserId, string DisplayName);

/// <summary>Where a file was: its folder, and whether that folder still exists (FR-005).</summary>
public sealed record TrashPlaceResponse(Guid? FolderId, string? FolderName, string FolderState);

/// <summary>One trashed file (trash-api.yaml TrashEntry).</summary>
public sealed record TrashEntryResponse(
    string Kind,
    Guid Id,
    string Label,
    string? Address,
    TrashPlaceResponse Place,
    DateTimeOffset TrashedAtUtc,
    TrashActorResponse TrashedBy,
    DateTimeOffset PurgeOnUtc,
    long SizeBytes,
    bool CanRestore,
    bool CanPurge);

/// <summary>A page of the media trash, with the storage the trashed files take (FR-012).</summary>
public sealed record TrashListResponse(IReadOnlyList<TrashEntryResponse> Entries, string? NextCursor, int Total, long TotalBytes);

/// <summary>Where restoring a file puts it (021 TR-10).</summary>
public sealed record RestorePlanResponse(string Placement, IReadOnlyList<string> PlacementOptions, Guid? FolderId, string? FolderName, bool FolderGone, string Address);

/// <summary>A restore request: original, top, or chosen with a folder, and the address the member saw.</summary>
public sealed record RestoreRequest(string? Placement, Guid? FolderId, string? ConfirmedAddress);

/// <summary>One restored file.</summary>
public sealed record RestoredResponse(string Kind, Guid Id, string? Address);

/// <summary>What a restore did.</summary>
public sealed record RestoreResultResponse(IReadOnlyList<RestoredResponse> Restored, bool Published);

/// <summary>The outcome of a bulk action, per entry (FR-019).</summary>
public sealed record BulkOutcomeResponse(IReadOnlyList<Guid> Succeeded, IReadOnlyList<BulkFailureResponse> Failed);

/// <summary>Why one entry of a bulk action failed.</summary>
public sealed record BulkFailureResponse(Guid Id, string Code, string Detail);

/// <summary>The trash entry kind of the Media module.</summary>
internal static class TrashKinds
{
    public const string File = "file";
}
