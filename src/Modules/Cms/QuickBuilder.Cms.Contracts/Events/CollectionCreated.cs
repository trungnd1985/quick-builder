using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Cms.Contracts.Events;

/// <summary>
/// A collection was created, empty, at revision 1. Published in the same transaction as the
/// change (001 FR-015, 014 research CS-14).
/// </summary>
/// <remarks>
/// Carries ids only — never names or schema content. <c>Cms.CollectionCreated</c> is a
/// published contract name: renaming it is a breaking change.
/// </remarks>
public sealed record CollectionCreated(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    int Revision,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.CollectionCreated";
}
