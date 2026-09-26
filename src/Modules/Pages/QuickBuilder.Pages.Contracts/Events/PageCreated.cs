using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// A page was added to a site's page tree. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// `ParentId` is null for a top-level page. Carries ids and paths only — never titles, names or content (010 FR-037).
/// <para>
/// <c>Pages.PageCreated</c> is a published contract name. Renaming it is a breaking change: add a V2
/// alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record PageCreated(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid PageId,
    Guid? ParentId,
    string Path,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.PageCreated";
}
