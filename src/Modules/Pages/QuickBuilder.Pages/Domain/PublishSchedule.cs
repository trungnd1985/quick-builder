using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// A publish or unpublish set for a future time (012 FR-018 to FR-021, research PR-14,
/// data-model §1.4, §4.2).
/// </summary>
/// <remarks>
/// A scheduled publish captures the draft when it is set, so what goes live is what the
/// member reviewed. The due message in the outbox carries <see cref="Revision"/>: changing
/// the time increments it, which turns the old message into a no-op.
/// </remarks>
internal sealed partial class PublishSchedule : ISiteOwned
{
    private PublishSchedule(Guid siteId, string subjectKind, Guid subjectId, string action, Guid createdBy, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        Action = action;
        State = ScheduleStates.Pending;
        CreatedBy = createdBy;
        CreatedAtUtc = now;
    }

    // EF Core materialisation.
    private PublishSchedule()
    {
        SubjectKind = string.Empty;
        Action = string.Empty;
        State = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string SubjectKind { get; private set; }

    public Guid SubjectId { get; private set; }

    /// <summary>One of <see cref="ScheduleActions"/>.</summary>
    public string Action { get; private set; }

    public DateTimeOffset DueAtUtc { get; private set; }

    public int Revision { get; private set; }

    /// <summary>One of <see cref="ScheduleStates"/>.</summary>
    public string State { get; private set; }

    public string? CapturedDocument { get; private set; }

    public int? CapturedSchemaVersion { get; private set; }

    public string? CapturedTitle { get; private set; }

    public long? CapturedDraftVersion { get; private set; }

    public string? FailureCode { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public bool IsPending => State == ScheduleStates.Pending;

    /// <summary>A scheduled publish, capturing the draft now (FR-019).</summary>
    public static PublishSchedule ForPublish(
        Guid siteId,
        string subjectKind,
        Guid subjectId,
        DateTimeOffset dueAtUtc,
        string document,
        int schemaVersion,
        string? title,
        long draftVersion,
        Guid createdBy,
        DateTimeOffset now)
    {
        var schedule = new PublishSchedule(siteId, subjectKind, subjectId, ScheduleActions.Publish, createdBy, now);
        schedule.Capture(document, schemaVersion, title, draftVersion);
        schedule.MoveTo(dueAtUtc);
        return schedule;
    }

    /// <summary>A scheduled unpublish of a page.</summary>
    public static PublishSchedule ForUnpublish(Guid siteId, Guid pageId, DateTimeOffset dueAtUtc, Guid createdBy, DateTimeOffset now)
    {
        var schedule = new PublishSchedule(siteId, SubjectKinds.Page, pageId, ScheduleActions.Unpublish, createdBy, now);
        schedule.MoveTo(dueAtUtc);
        return schedule;
    }

    /// <summary>Changes a pending schedule's time; the old due message becomes a no-op.</summary>
    public void Reschedule(DateTimeOffset dueAtUtc, Guid by)
    {
        EnsurePending();
        CreatedBy = by;
        MoveTo(dueAtUtc);
    }

    /// <summary>Replaces what a pending scheduled publish will publish with the draft as it is now.</summary>
    public void Recapture(string document, int schemaVersion, string? title, long draftVersion)
    {
        EnsurePending();
        Capture(document, schemaVersion, title, draftVersion);
    }

    /// <summary>Whether the message carrying <paramref name="revision"/> may still run this schedule.</summary>
    public bool IsDue(int revision) => IsPending && Revision == revision;

    public void Complete(DateTimeOffset now) => Finish(ScheduleStates.Done, failureCode: null, now);

    public void Cancel(DateTimeOffset now) => Finish(ScheduleStates.Cancelled, failureCode: null, now);

    public void Fail(string failureCode, DateTimeOffset now) => Finish(ScheduleStates.Failed, failureCode, now);

    private void Capture(string document, int schemaVersion, string? title, long draftVersion)
    {
        CapturedDocument = document;
        CapturedSchemaVersion = schemaVersion;
        CapturedTitle = title;
        CapturedDraftVersion = draftVersion;
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
