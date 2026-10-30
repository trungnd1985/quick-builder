using System.Text.Json;

namespace QuickBuilder.Pages.Features;

// The HTTP shapes of contracts/pages-api.yaml. Public because controllers return them; named
// *Response because that is the module surface the architecture tests allow.

/// <summary>What the caller may do on this site, decided per request (research P-13).</summary>
/// <remarks>The admin shell hides controls from this. The server checks every action regardless.</remarks>
/// <remarks>012 adds <see cref="CanPublish"/> and <see cref="CanViewPublishLog"/> (research PR-18).</remarks>
public sealed record PermissionsResponse(
    bool CanManageStructure,
    bool CanEditPageDrafts,
    bool CanManageComponents,
    bool CanPublish,
    bool CanViewPublishLog);

/// <summary>One page in the flat, depth-first tree (research P-16).</summary>
/// <remarks>012 adds <see cref="Publication"/>; 016 adds <see cref="TemplateCollectionId"/>: the collection a template page shows, else null.</remarks>
public sealed record PageNodeResponse(
    Guid Id,
    Guid? ParentId,
    int Position,
    int Depth,
    string Title,
    string Slug,
    string Path,
    bool IsHome,
    int ChildCount,
    int Version,
    PublicationResponse Publication,
    Guid? TemplateCollectionId = null);

/// <summary>A site's whole page tree: parents before children, siblings by position.</summary>
public sealed record PageTreeResponse(IReadOnlyList<PageNodeResponse> Pages, PermissionsResponse Permissions)
{
    /// <summary>016 TB-12: what the change did to item addresses; empty when nothing.</summary>
    public IReadOnlyList<SetPageTemplate.TemplateWarningResponse> Warnings { get; init; } = [];

    /// <summary>017 FR-028: what the accepted redirect offer did; null when none was accepted.</summary>
    public RedirectOutcomeResponse? Redirects { get; init; }
}

/// <summary>Who did something. <see cref="DisplayName"/> is null if the name cannot be found.</summary>
public sealed record UserReferenceResponse(Guid Id, string? DisplayName);

/// <summary>A draft without its document.</summary>
/// <summary>023 FR-021: where a copy came from: the source, the version the member had loaded (by when it was saved), who and when.</summary>
public sealed record CopyOriginResponse(Guid SourceId, DateTimeOffset SourceSavedAtUtc, UserReferenceResponse CopiedBy, DateTimeOffset CopiedAtUtc);

public sealed record DraftSummaryResponse(int SchemaVersion, uint Version, DateTimeOffset UpdatedAtUtc, UserReferenceResponse UpdatedBy);

/// <summary>One page with its draft's metadata.</summary>
public sealed record PageDetailResponse(
    Guid Id,
    Guid? ParentId,
    int Position,
    int Depth,
    string Title,
    string Slug,
    string Path,
    bool IsHome,
    int ChildCount,
    int Version,
    DraftSummaryResponse Draft,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PermissionsResponse Permissions,
    PublicationResponse Publication,
    Guid? TemplateCollectionId = null)
{
    /// <summary>016 TB-12: what the change did to item addresses; empty when nothing.</summary>
    public IReadOnlyList<SetPageTemplate.TemplateWarningResponse> Warnings { get; init; } = [];

    /// <summary>023 FR-021: set on a page made by "save my version as a copy".</summary>
    public CopyOriginResponse? CopyOrigin { get; init; }

    /// <summary>017 FR-028: what the accepted redirect offer did; null when none was accepted.</summary>
    public RedirectOutcomeResponse? Redirects { get; init; }
}

/// <summary>A draft and its document, equal as JSON to what was saved (FR-020).</summary>
public sealed record DraftResponse(
    int SchemaVersion,
    uint Version,
    DateTimeOffset UpdatedAtUtc,
    UserReferenceResponse UpdatedBy,
    JsonElement Document);

/// <summary>One shared component.</summary>
public sealed record ComponentResponse(
    Guid Id,
    string Name,
    int UsageCount,
    uint Version,
    DraftSummaryResponse Draft,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PublicationResponse Publication)
{
    /// <summary>023 FR-021: set on a component made by "save my version as a copy".</summary>
    public CopyOriginResponse? CopyOrigin { get; init; }
}

/// <summary>A site's components, by name.</summary>
public sealed record ComponentListResponse(IReadOnlyList<ComponentResponse> Components, PermissionsResponse Permissions);

/// <summary>A page that uses a component.</summary>
public sealed record PageReferenceResponse(Guid Id, string Title, string Path);

/// <summary>What happened to the redirects a change offered and the member accepted (017 FR-028 to FR-031).</summary>
/// <param name="Created">Redirects added.</param>
/// <param name="Skipped">Old addresses left alone because live content now holds them (FR-031).</param>
/// <param name="Retargeted">Existing redirects pointed at the new address instead.</param>
/// <param name="TurnedOff">Existing redirects turned off because they would now loop.</param>
public sealed record RedirectOutcomeResponse(int Created, int Skipped, int Retargeted, IReadOnlyList<Guid> TurnedOff)
{
    public static RedirectOutcomeResponse None { get; } = new(0, 0, 0, []);
}
