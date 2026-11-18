namespace QuickBuilder.Editor.Features.PreviewLinks;

/// <summary>A member, as preview link lists name them.</summary>
public sealed record PreviewLinkUserResponse(Guid Id, string DisplayName);

/// <summary>A preview link without its secret (contracts/preview-api.yaml, <c>PreviewLink</c>).</summary>
public sealed record PreviewLinkResponse(
    Guid Id,
    string DocumentKind,
    Guid DocumentId,
    int? VersionNumber,
    Guid? SampleItemId,
    string? Label,
    PreviewLinkUserResponse CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? LastOpenedAtUtc);

/// <summary>A created link, and its full address: the only time the secret is ever shown (FR-014).</summary>
public sealed record CreatedPreviewLinkResponse(PreviewLinkResponse Link, string Url);

/// <summary>A link of the site-wide list, with what it shows (FR-015).</summary>
/// <param name="TargetLabel">The page's current title or the item's name; content, render it encoded.</param>
public sealed record SitePreviewLinkResponse(PreviewLinkResponse Link, string? TargetLabel, Guid? CollectionId);

/// <summary>One page of the site-wide list.</summary>
public sealed record SitePreviewLinksResponse(IReadOnlyList<SitePreviewLinkResponse> Items, string? NextCursor);

/// <summary>How many links a revoke-all ended.</summary>
public sealed record RevokedPreviewLinksResponse(int Revoked);

/// <summary>A new link (contracts/preview-api.yaml, <c>CreatePreviewLink</c>).</summary>
public sealed record CreatePreviewLinkRequest(
    string? DocumentKind,
    Guid DocumentId,
    int? VersionNumber,
    Guid? SampleItemId,
    string? Label,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Which links a revoke-all ends: one page's or item's, or, with neither, the whole site's.</summary>
public sealed record RevokeAllPreviewLinksRequest(string? DocumentKind, Guid? DocumentId);
