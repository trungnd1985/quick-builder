using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Editor.Contracts.Events;

/// <summary>
/// A member created a preview link (022 research PV-16, spec FR-016). Published in the same
/// transaction as the link.
/// </summary>
/// <remarks>
/// The Pages module records it in the page's history, the Cms module in the item's. Ids, a
/// number and a time only: never the secret, and never the label, which is free text a member
/// typed and could name the reviewer.
/// <para>
/// <c>Editor.PreviewLinkCreated</c> is a published contract name: renaming it is a breaking change.
/// </para>
/// </remarks>
/// <param name="DocumentKind"><c>page</c> or <c>item</c>.</param>
/// <param name="VersionNumber">The version the link shows; null for the draft.</param>
public sealed record PreviewLinkCreated(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    string DocumentKind,
    Guid DocumentId,
    Guid LinkId,
    int? VersionNumber,
    DateTimeOffset ExpiresAtUtc,
    Guid By) : IIntegrationEvent
{
    public static string EventType => "Editor.PreviewLinkCreated";
}

/// <summary>
/// A member revoked a preview link (022 research PV-16, spec FR-016). Only an explicit revoke:
/// a link that expires, or whose creator lost access, raises nothing.
/// </summary>
/// <remarks><c>Editor.PreviewLinkRevoked</c> is a published contract name.</remarks>
public sealed record PreviewLinkRevoked(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    string DocumentKind,
    Guid DocumentId,
    Guid LinkId,
    int? VersionNumber,
    Guid By) : IIntegrationEvent
{
    public static string EventType => "Editor.PreviewLinkRevoked";
}
