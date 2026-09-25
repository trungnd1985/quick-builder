using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Sites.Contracts.Events;

/// <summary>
/// A disabled site was put back on the public surface. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// Nothing was lost while it was disabled, so there is nothing to restore. Identity records it as a security event (009 research S-11).
/// <para>
/// <c>Sites.SiteEnabled</c> is a published contract name. Renaming it is a breaking change: add
/// a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record SiteEnabled(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Sites.SiteEnabled";
}
