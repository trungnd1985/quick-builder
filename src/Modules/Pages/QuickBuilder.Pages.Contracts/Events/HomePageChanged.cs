using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// A page became its site's home page. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// `PreviousPageId` is null when the site had no home page before. Carries ids and paths only — never titles, names or content (010 FR-037).
/// <para>
/// <c>Pages.HomePageChanged</c> is a published contract name. Renaming it is a breaking change: add a V2
/// alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record HomePageChanged(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid PageId,
    Guid? PreviousPageId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.HomePageChanged";
}
