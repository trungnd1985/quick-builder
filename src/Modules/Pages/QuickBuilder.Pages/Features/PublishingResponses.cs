using System.Text.Json;

namespace QuickBuilder.Pages.Features;

// The HTTP shapes of 012 contracts/publishing-api.yaml. Public because controllers return
// them; named *Response because that is the module surface the architecture tests allow.

/// <summary>A pending or failed schedule.</summary>
public sealed record ScheduleResponse(
    string Action,
    DateTimeOffset DueAtUtc,
    string State,
    string? FailureCode,
    UserReferenceResponse SetBy,
    long? CapturedDraftVersion);

/// <summary>Whether and what visitors see of a page or component (FR-007).</summary>
public sealed record PublicationResponse(
    string State,
    int? LiveNumber,
    DateTimeOffset? LiveSinceUtc,
    UserReferenceResponse? LiveBy,
    bool HasUnpublishedChanges,
    string? LiveUrl,
    IReadOnlyList<ScheduleResponse> PendingSchedules,
    ScheduleResponse? FailedSchedule)
{
    /// <summary>Never published and nothing scheduled.</summary>
    public static PublicationResponse Draft { get; } = new("draft", null, null, null, false, null, [], null);

    /// <summary>016 TB-12: what unpublishing did to item addresses; empty when nothing.</summary>
    public IReadOnlyList<SetPageTemplate.TemplateWarningResponse> Warnings { get; init; } = [];
}

/// <summary>One publish log entry (FR-012, FR-017).</summary>
public sealed record LogEntryResponse(
    Guid Id,
    string SubjectKind,
    Guid SubjectId,
    string SubjectLabel,
    string? SubjectPath,
    bool SubjectExists,
    string Action,
    int? VersionNumber,
    int? RestoredFromNumber,
    UserReferenceResponse? Actor,
    UserReferenceResponse? ScheduleOwner,
    string? ReasonCode,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset? ExpiresAtUtc = null);

/// <summary>A page's or component's history (FR-016).</summary>
public sealed record SubjectHistoryResponse(
    PublicationResponse Publication,
    IReadOnlyList<int> RetainedVersions,
    IReadOnlyList<LogEntryResponse> Entries);

/// <summary>One retained version's content.</summary>
public sealed record VersionContentResponse(
    int Number,
    int SchemaVersion,
    JsonElement Document,
    string? Title,
    string Kind,
    int? RestoredFromNumber,
    DateTimeOffset PublishedAtUtc,
    UserReferenceResponse PublishedBy);

/// <summary>A page of the site's publish log, newest first.</summary>
public sealed record PublishLogResponse(IReadOnlyList<LogEntryResponse> Items, string? NextCursor);
