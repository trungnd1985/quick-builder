using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// One recorded publishing action (012 FR-012, FR-017, data-model §1.5).
/// </summary>
/// <remarks>
/// Append-only, with no foreign key to its subject, so it outlives deleted pages and
/// components (clarification Q2). The label and path are taken at the moment of the action,
/// so the log still reads sensibly after both have changed or gone.
/// </remarks>
internal sealed class PublishLogEntry : ISiteOwned
{
    internal PublishLogEntry(
        Guid id,
        Guid siteId,
        string subjectKind,
        Guid subjectId,
        string subjectLabel,
        string? subjectPath,
        string action,
        DateTimeOffset occurredAtUtc)
    {
        Id = id;
        SiteId = siteId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        SubjectLabel = subjectLabel;
        SubjectPath = subjectPath;
        Action = action;
        OccurredAtUtc = occurredAtUtc;
    }

    // EF Core materialisation.
    private PublishLogEntry()
    {
        SubjectKind = string.Empty;
        SubjectLabel = string.Empty;
        Action = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>One of <see cref="SubjectKinds"/>.</summary>
    public string SubjectKind { get; private set; }

    /// <summary>The page or component, or the site for a design entry.</summary>
    public Guid SubjectId { get; private set; }

    public string SubjectLabel { get; private set; }

    public string? SubjectPath { get; private set; }

    /// <summary>One of <see cref="LogActions"/>.</summary>
    public string Action { get; private set; }

    public int? VersionNumber { get; init; }

    public int? RestoredFromNumber { get; init; }

    /// <summary>Who acted; null when a schedule acted, which records <see cref="ScheduleOwnerId"/>.</summary>
    public Guid? ActorUserId { get; init; }

    public Guid? ScheduleOwnerId { get; init; }

    /// <summary>A failure code, or <see cref="LogReasons.SupersededByManualAction"/>.</summary>
    public string? ReasonCode { get; init; }

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
