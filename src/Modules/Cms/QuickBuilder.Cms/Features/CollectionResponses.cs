using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.Cms.Features;

/// <summary>A collection as the list shows it (contracts/cms-api.yaml, <c>CollectionSummary</c>).</summary>
public sealed record CollectionSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    int FieldCount,
    int ItemCount,
    int Revision,
    DateTimeOffset UpdatedAtUtc,
    int TrashedItemCount = 0);

/// <summary>The site's collections, and whether the caller may change them.</summary>
public sealed record CollectionListResponse(IReadOnlyList<CollectionSummaryResponse> Collections, bool CanManage);

/// <summary>Who made a change.</summary>
public sealed record PersonResponse(Guid Id, string DisplayName);

/// <summary>One collection with its schema and revision (contracts/cms-api.yaml, <c>Collection</c>).</summary>
/// <param name="CanManage">Whether the caller may change it; the builder is read-only otherwise.</param>
/// <param name="IdMap">After a save: each temporary id of the proposal and its permanent id. Empty otherwise.</param>
/// <param name="SchemaRevision">The revision at which the schema last changed; item forms send it back (015 research IT-07).</param>
public sealed record CollectionResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    JsonNode Schema,
    int Revision,
    int SchemaRevision,
    int ItemCount,
    bool CanManage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PersonResponse UpdatedBy,
    IReadOnlyDictionary<string, string> IdMap,
    int TrashedItemCount = 0);

/// <summary>A collection's details, as created (contracts/cms-api.yaml, <c>CollectionDetailsInput</c>).</summary>
public sealed record CollectionDetailsRequest(string? Name, string? Slug, string? Description);

/// <summary>The member's confirmation of a preview: its digest (research CS-08).</summary>
public sealed record ConfirmationRequest(string? Digest);

/// <summary>A proposed definition: details and schema, as one save (contracts/cms-api.yaml, <c>SaveRequest</c>).</summary>
public sealed record SaveCollectionRequest(
    int Revision,
    string? Name,
    string? Slug,
    string? Description,
    JsonElement? Schema,
    IReadOnlyDictionary<string, JsonElement>? FillIns,
    ConfirmationRequest? Confirmation);
