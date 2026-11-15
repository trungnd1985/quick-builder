using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

/// <summary>
/// A page was taken out of the trash (021 TR-08). It comes back unpublished, so nothing public
/// changes until it is published. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// Carries ids only — never titles, names, addresses or content. No module consumes it in 021;
/// it is published for the registry and for future consumers.
/// <para>
/// <c>Pages.PageRestored</c> is a published contract name. Renaming it is a breaking change.
/// </para>
/// </remarks>
public sealed record PageRestored(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid PageId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.PageRestored";
}
