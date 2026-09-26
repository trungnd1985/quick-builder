using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Sites.Contracts.Events;

/// <summary>
/// A host name was removed from a site. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// From this event on, public requests for the name no longer reach the site. Identity records it as a security event (009 research S-11).
/// <para>
/// <c>Sites.HostNameRemoved</c> is a published contract name. Renaming it is a breaking change: add
/// a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record HostNameRemoved(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    string HostName,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Sites.HostNameRemoved";
}
