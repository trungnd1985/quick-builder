using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Sites.Contracts.Events;

/// <summary>
/// A host name was added to an existing site. Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// Creation publishes only <c>Sites.SiteCreated</c>, which already lists its host names. Identity records it as a security event (009 research S-11).
/// <para>
/// <c>Sites.HostNameAdded</c> is a published contract name. Renaming it is a breaking change: add
/// a V2 alongside and retire this once nothing subscribes.
/// </para>
/// </remarks>
public sealed record HostNameAdded(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    string HostName,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Sites.HostNameAdded";
}
