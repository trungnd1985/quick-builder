using System.Text.Json;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>What a schedule does (015 FR-024).</summary>
internal static class ItemScheduleActions
{
    public const string Publish = "publish";
    public const string Unpublish = "unpublish";

    public static bool IsKnown(string action) => action is Publish or Unpublish;
}

/// <summary>Where a schedule is in its life (015 data-model §3.4).</summary>
internal static class ItemScheduleStates
{
    public const string Pending = "pending";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

/// <summary>
/// A publish or unpublish of an item set for a future time (015 FR-024 to FR-027, research IT-09),
/// modelled on 012's <c>PublishSchedule</c>.
/// </summary>
/// <remarks>
/// A scheduled publish captures the draft when it is set, so what goes live is what the member
/// reviewed (FR-025). The due message in the Cms outbox carries <see cref="Revision"/>: moving
/// the time increments it, which turns the old message into a no-op.
/// </remarks>
internal sealed partial class ItemSchedule : ISiteOwned, IDisposable
{
    private ItemSchedule(Guid siteId, Guid itemId, string action, Guid createdBy, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        SiteId = siteId;
        ItemId = itemId;
        Action = action;
        State = ItemScheduleStates.Pending;
        CreatedBy = createdBy;
        CreatedAtUtc = now;
    }

    // EF Core materialisation.
    private ItemSchedule()
    {
        Action = string.Empty;
        State = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>One of <see cref="ItemScheduleActions"/>.</summary>
    public string Action { get; private set; }

    public DateTimeOffset DueAtUtc { get; private set; }

    public int Revision { get; private set; }

    /// <summary>One of <see cref="ItemScheduleStates"/>.</summary>
    public string State { get; private set; }

    /// <summary>For a publish: the draft values when set or last recaptured. Migrated by schema changes.</summary>
    public JsonDocument? CapturedValues { get; private set; }

    public string? CapturedSlug { get; private set; }

    public string? FailureCode { get; private set; }

    /// <summary>
    /// For a publish: whether a changed slug should redirect from the old address once the
    /// schedule runs (017 FR-029, research SE-11).
    /// </summary>
    public bool RedirectOldAddress { get; private set; }

    /// <summary>For an unpublish: the page the item's address should redirect to (017 FR-032).</summary>
    public Guid? RedirectToPageId { get; private set; }

    /// <summary>Who set the schedule, or last moved it.</summary>
    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public bool IsPending => State == ItemScheduleStates.Pending;

    public static ItemSchedule ForPublish(Guid siteId, Guid itemId, DateTimeOffset dueAtUtc, ItemValues draft, string slug, Guid by, DateTimeOffset now)
    {
        var schedule = new ItemSchedule(siteId, itemId, ItemScheduleActions.Publish, by, now);
        schedule.Capture(draft, slug);
        schedule.MoveTo(dueAtUtc);
        return schedule;
    }

    public static ItemSchedule ForUnpublish(Guid siteId, Guid itemId, DateTimeOffset dueAtUtc, Guid by, DateTimeOffset now)
    {
        var schedule = new ItemSchedule(siteId, itemId, ItemScheduleActions.Unpublish, by, now);
        schedule.MoveTo(dueAtUtc);
        return schedule;
    }

    public ItemValues? ReadCaptured() => CapturedValues is null ? null : ItemValues.Read(CapturedValues);

    /// <summary>
    /// The redirect accepted for when the schedule runs (017 FR-029, FR-032): a publish's changed
    /// slug, or an unpublish's address to a page. The other action's choice is ignored.
    /// </summary>
    public void RedirectOnRun(bool redirectOldAddress, Guid? redirectToPageId)
    {
        EnsurePending();
        RedirectOldAddress = Action == ItemScheduleActions.Publish && redirectOldAddress;
        RedirectToPageId = Action == ItemScheduleActions.Unpublish ? redirectToPageId : null;
    }

    /// <summary>Changes a pending schedule's time; the old due message becomes a no-op.</summary>
    public void Reschedule(DateTimeOffset dueAtUtc, Guid by)
    {
        EnsurePending();
        CreatedBy = by;
        MoveTo(dueAtUtc);
    }

    /// <summary>Replaces what a pending scheduled publish will publish with the draft as it is now.</summary>
    public void Recapture(ItemValues draft, string slug)
    {
        EnsurePending();
        Capture(draft, slug);
    }

    /// <summary>Stores captured values converted by a schema change (research IT-12).</summary>
    public void Migrate(ItemValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        CapturedValues = values.ToDocument();
    }

    /// <summary>Whether the message carrying <paramref name="revision"/> may still run this schedule.</summary>
    public bool IsDue(int revision) => IsPending && Revision == revision;

    public void Complete(DateTimeOffset now) => Finish(ItemScheduleStates.Done, null, now);

    public void Cancel(DateTimeOffset now) => Finish(ItemScheduleStates.Cancelled, null, now);

    public void Fail(string failureCode, DateTimeOffset now) => Finish(ItemScheduleStates.Failed, failureCode, now);

    public void Dispose() => CapturedValues?.Dispose();

    private void Capture(ItemValues draft, string slug)
    {
        ArgumentNullException.ThrowIfNull(draft);
        CapturedValues = draft.ToDocument();
        CapturedSlug = slug;
    }

    private void MoveTo(DateTimeOffset dueAtUtc)
    {
        DueAtUtc = dueAtUtc;
        Revision++;
    }

    private void Finish(string state, string? failureCode, DateTimeOffset now)
    {
        EnsurePending();
        State = state;
        FailureCode = failureCode;
        CompletedAtUtc = now;
    }

    private void EnsurePending()
    {
        if (!IsPending)
        {
            throw new InvalidOperationException($"The schedule is {State}, not pending; the caller checks first.");
        }
    }
}
