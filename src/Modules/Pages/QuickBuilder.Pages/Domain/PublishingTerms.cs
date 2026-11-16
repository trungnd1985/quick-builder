namespace QuickBuilder.Pages.Domain;

/// <summary>What a publication, version, schedule or log entry is about (012 data-model §1).</summary>
/// <remarks>Stored as these lower-case words, which the API returns unchanged.</remarks>
internal static class SubjectKinds
{
    public const string Page = "page";

    public const string Component = "component";

    /// <summary>A menu (024 research NV-01): published like a component.</summary>
    public const string Menu = "menu";

    /// <summary>The site's design: log entries only, recorded from the Editor module's event.</summary>
    public const string Design = "design";
}

/// <summary>How a published version came about (012 data-model §1.2).</summary>
internal static class VersionKinds
{
    public const string Publish = "publish";

    public const string Scheduled = "scheduled";

    public const string Rollback = "rollback";
}

/// <summary>What a live version depends on (012 research PR-04).</summary>
internal static class DependencyKinds
{
    public const string Component = "component";

    public const string Page = "page";

    public const string Media = "media";

    /// <summary>A Collection list's source collection (016 data-model §1.2).</summary>
    public const string Collection = "collection";

    /// <summary>A template page's own collection (016 data-model §1.2).</summary>
    public const string Template = "template";

    /// <summary>A Navigation block's menu, from a page or component (024 research NV-07).</summary>
    public const string Menu = "menu";

    /// <summary>A menu entry's collection item (024 research NV-07).</summary>
    public const string Item = "item";
}

/// <summary>What a schedule does (012 data-model §1.4).</summary>
internal static class ScheduleActions
{
    public const string Publish = "publish";

    public const string Unpublish = "unpublish";
}

/// <summary>Where a schedule is in its life (012 data-model §4.2).</summary>
internal static class ScheduleStates
{
    public const string Pending = "pending";

    public const string Done = "done";

    public const string Cancelled = "cancelled";

    public const string Failed = "failed";
}

/// <summary>The actions the publish log records (012 data-model §1.5).</summary>
internal static class LogActions
{
    public const string Publish = "publish";

    public const string ScheduledPublish = "scheduled_publish";

    public const string Rollback = "rollback";

    public const string Unpublish = "unpublish";

    public const string ScheduledUnpublish = "scheduled_unpublish";

    public const string ScheduleSet = "schedule_set";

    public const string ScheduleChanged = "schedule_changed";

    public const string ScheduleCancelled = "schedule_cancelled";

    public const string ScheduleFailed = "schedule_failed";

    public const string CopiedToDraft = "copied_to_draft";

    public const string Deleted = "deleted";

    /// <summary>Moved to the trash (021 TR-07).</summary>
    public const string Trashed = "trashed";

    /// <summary>Taken out of the trash (021 TR-07).</summary>
    public const string Restored = "restored";

    /// <summary>Deleted permanently from the trash, by an Administrator or the purge (021 TR-14).</summary>
    public const string Purged = "purged";
}

/// <summary>Why a schedule was cancelled without anyone cancelling it (012 FR-019).</summary>
internal static class LogReasons
{
    public const string SupersededByManualAction = "superseded_by_manual_action";

    /// <summary>021 TR-07: the subject was moved to the trash.</summary>
    public const string Trashed = "trashed";
}
