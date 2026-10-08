using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.Cms.Features.Items;

// The shapes of contracts/items-api.yaml (015). Names, slugs and values are content: the
// admin renders them as text, and nothing here is ever logged.

/// <summary>A member, by id and display name.</summary>
public sealed record MemberResponse(Guid Id, string Name);

/// <summary>A pending schedule of an item.</summary>
public sealed record ScheduleResponse(string Action, DateTimeOffset DueAtUtc, MemberResponse SetBy);

/// <summary>One problem of an item's report; <c>FieldId</c> is a field id or <c>slug</c>.</summary>
public sealed record ProblemResponse(string FieldId, string Problem, decimal? Limit);

/// <summary>Everything wrong with an item's draft (research IT-05).</summary>
public sealed record ReportResponse(IReadOnlyList<ProblemResponse> Problems);

/// <summary>One row of the item list.</summary>
public sealed record ItemSummaryResponse(
    Guid Id,
    string? Name,
    string Slug,
    string Status,
    bool Attention,
    IReadOnlyList<ScheduleResponse> Schedules,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    MemberResponse UpdatedBy,
    DateTimeOffset? PublishedAtUtc);

/// <summary>One page of a collection's items.</summary>
public sealed record ItemListResponse(IReadOnlyList<ItemSummaryResponse> Items, int Total, int Page, int PageSize);

/// <summary>An item with its draft, published values, report and schedules.</summary>
/// <param name="SchemaRevision">The collection's schema revision; a draft save sends it back (research IT-07).</param>
/// <param name="ScheduledDiffersFromDraft">A publish is scheduled and its captured content differs from the draft (FR-025).</param>
/// <param name="UnpublishedReferences">After a publish: the referenced items that are not published (FR-020). Empty otherwise.</param>
public sealed record ItemResponse(
    Guid Id,
    Guid CollectionId,
    string? Name,
    string Slug,
    string Status,
    bool Attention,
    IReadOnlyList<ScheduleResponse> Schedules,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    MemberResponse UpdatedBy,
    DateTimeOffset? PublishedAtUtc,
    int Revision,
    int SchemaRevision,
    JsonNode Draft,
    JsonNode? Published,
    string? PublishedSlug,
    int? LiveVersion,
    ReportResponse Report,
    bool ScheduledDiffersFromDraft,
    IReadOnlyList<ItemRefResponse> UnpublishedReferences);

/// <summary>An item named by id, for reference pickers and chips.</summary>
public sealed record ItemRefResponse(Guid Id, Guid CollectionId, string? Name, string Slug, string Status);

/// <summary>Items found by id, and the ids not found.</summary>
public sealed record ItemLookupResponse(IReadOnlyList<ItemRefResponse> Items, IReadOnlyList<Guid> Missing);

/// <summary>An item that refers to another.</summary>
public sealed record ReferrerResponse(
    Guid ItemId,
    string? ItemName,
    string ItemSlug,
    Guid CollectionId,
    string CollectionName,
    string FieldId,
    string FieldName,
    bool InDraft,
    bool InPublished,
    bool InScheduled);

/// <summary>Up to 50 referrers, and how many there are.</summary>
public sealed record ReferrerListResponse(IReadOnlyList<ReferrerResponse> Referrers, int Total);

/// <summary>Whether a slug is free, and the slug to suggest.</summary>
public sealed record SlugAvailabilityResponse(bool Available, string Suggestion);

/// <summary>A draft as the form sends it (contracts/items-api.yaml, <c>DraftInput</c>).</summary>
public sealed record DraftRequest(string? Slug, JsonElement? Values, int SchemaRevision, int Revision);

/// <summary>A publish, unpublish or rollback (contracts/items-api.yaml, <c>PublishingInput</c>).</summary>
public sealed record PublishingRequest(int Revision, bool CancelSchedule);

/// <summary>A change that only needs the item's revision.</summary>
public sealed record RevisionRequest(int Revision);

/// <summary>Setting or moving a schedule.</summary>
public sealed record ScheduleRequest(DateTimeOffset DueAtUtc, int Revision, bool Recapture);

/// <summary>Ids to look up.</summary>
public sealed record LookupRequest(IReadOnlyList<Guid>? Ids);
