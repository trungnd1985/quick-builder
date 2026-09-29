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
}
