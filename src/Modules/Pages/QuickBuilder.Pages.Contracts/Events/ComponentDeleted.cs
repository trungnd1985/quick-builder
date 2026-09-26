using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// A shared component was permanently deleted. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// No page draft referenced it: the database refuses the delete otherwise (research P-11). Carries ids and paths only — never titles, names or content (010 FR-037).
/// <para>
/// <c>Pages.ComponentDeleted</c> is a published contract name. Renaming it is a breaking change: add a V2
/// alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record ComponentDeleted(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid ComponentId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.ComponentDeleted";
}
