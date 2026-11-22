using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>What an item history entry records (015 data-model §3.5).</summary>
internal static class ItemHistoryKinds
{
    public const string Created = "created";
    public const string Published = "published";
    public const string RolledBack = "rolledBack";
    public const string Unpublished = "unpublished";
    public const string Scheduled = "scheduled";
    public const string Rescheduled = "rescheduled";
    public const string ScheduleCancelled = "scheduleCancelled";
    public const string ScheduleFailed = "scheduleFailed";
    public const string CopiedToDraft = "copiedToDraft";

    /// <summary>023 FR-020: created by "save my version as a copy"; the item's copy origin says from what.</summary>
    public const string CreatedAsCopy = "createdAsCopy";

    /// <summary>021 TR-07: moved to the trash.</summary>
    public const string Trashed = "trashed";

    /// <summary>021 TR-07: taken out of the trash.</summary>
    public const string Restored = "restored";

    /// <summary>022 FR-016: a member created a preview link to the item.</summary>
    public const string PreviewLinkCreated = "previewLinkCreated";

    /// <summary>022 FR-016: a member revoked a preview link to the item.</summary>
    public const string PreviewLinkRevoked = "previewLinkRevoked";
}

/// <summary>
/// One event in an item's history (015 FR-031): who did what, when. Append-only.
/// </summary>
/// <remarks>
/// It holds ids, kinds, numbers and codes only, never values, names or slugs (FR-032). A
/// <c>published</c> or <c>unpublished</c> entry that came from a schedule has
/// <see cref="ScheduleAction"/> set to that schedule's action. Draft saves are not entries;
/// the item's own last-change fields show the latest.
/// </remarks>
internal sealed partial class ItemHistoryEntry : ISiteOwned
{
    private ItemHistoryEntry(Guid siteId, Guid itemId, string kind, Guid by, DateTimeOffset at)
    {
        Id = MonotonicIds.Next(at);
        SiteId = siteId;
        ItemId = itemId;
        Kind = kind;
        By = by;
        AtUtc = at;
    }

    // EF Core materialisation.
    private ItemHistoryEntry()
    {
        Kind = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>One of <see cref="ItemHistoryKinds"/>.</summary>
    public string Kind { get; private set; }

    public int? VersionNumber { get; private set; }

    public string? ScheduleAction { get; private set; }

    public DateTimeOffset? DueAtUtc { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    /// <summary>Who did it; for a scheduled run, who set the schedule.</summary>
    public Guid By { get; private set; }

    /// <summary>
    /// A preview link created or revoked (022 FR-016, research PV-16), with the event's id, so a
    /// redelivery adds nothing. A created link's expiry rides in <see cref="DueAtUtc"/>.
    /// </summary>
    public static ItemHistoryEntry ForPreviewLink(
        Guid eventId, Guid siteId, Guid itemId, string kind, Guid by, DateTimeOffset at, int? versionNumber, DateTimeOffset? expiresAtUtc) =>
        new(siteId, itemId, kind, by, at)
        {
            Id = eventId,
            VersionNumber = versionNumber,
            DueAtUtc = expiresAtUtc,
        };

    public static ItemHistoryEntry For(
        Item item,
        string kind,
        Guid by,
        DateTimeOffset at,
        int? versionNumber = null,
        string? scheduleAction = null,
        DateTimeOffset? dueAtUtc = null,
        string? failureCode = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ItemHistoryEntry(item.SiteId, item.Id, kind, by, at)
        {
            VersionNumber = versionNumber,
            ScheduleAction = scheduleAction,
            DueAtUtc = dueAtUtc,
            FailureCode = failureCode,
        };
    }
}
