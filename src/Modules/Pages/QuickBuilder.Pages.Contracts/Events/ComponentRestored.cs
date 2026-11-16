using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// A shared component was taken out of the trash (021 TR-08). It comes back unpublished.
/// Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// Carries ids only. No module consumes it in 021.
/// <para>
/// <c>Pages.ComponentRestored</c> is a published contract name. Renaming it is a breaking change.
/// </para>
/// </remarks>
public sealed record ComponentRestored(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid ComponentId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.ComponentRestored";
}
