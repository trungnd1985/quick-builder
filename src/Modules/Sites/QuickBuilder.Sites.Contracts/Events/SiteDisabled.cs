using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Sites.Contracts.Events;

/// <summary>
/// A site was taken off the public surface. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// Its data, grants and host names are kept; only public resolution stops. Identity records it as a security event (009 research S-11).
/// <para>
/// <c>Sites.SiteDisabled</c> is a published contract name. Renaming it is a breaking change: add
/// a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record SiteDisabled(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Sites.SiteDisabled";
}
