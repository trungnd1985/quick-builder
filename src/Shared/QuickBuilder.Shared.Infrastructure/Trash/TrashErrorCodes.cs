namespace QuickBuilder.Shared.Infrastructure.Trash;

/// <summary>
/// The error codes every trash shares, whichever module owns the content (021 data-model §5).
/// </summary>
/// <remarks>
/// Each module builds its own <c>Error</c> values from these codes, so the fields a refusal
/// carries go through that module's own error extensions, and maps them to the same status.
/// </remarks>
public static class TrashErrorCodes
{
    /// <summary>404: the entry is not in the trash (restored, purged, or never trashed).</summary>
    public const string NotInTrash = "trash.not_in_trash";

    /// <summary>409: the confirmed address or placement no longer holds; the body carries the new plan.</summary>
    public const string PlanChanged = "trash.plan_changed";

    /// <summary>422: the original parent or folder is in the trash or gone, and no placement was given.</summary>
    public const string PlacementRequired = "trash.placement_required";

    /// <summary>422: the chosen parent or folder is not live, or is under the page itself.</summary>
    public const string InvalidPlacement = "trash.invalid_placement";

    /// <summary>409: emptying the trash with an expected count that no longer matches.</summary>
    public const string CountChanged = "trash.count_changed";

    /// <summary>The HTTP status of a trash code, or null for any other code.</summary>
    public static int? StatusFor(string code) => code switch
    {
        NotInTrash => 404,
        PlanChanged or CountChanged => 409,
        PlacementRequired or InvalidPlacement => 422,
        _ => null,
    };
}
