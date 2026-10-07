using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Cms.Contracts.Events;

/// <summary>
/// A collection, its schema, its history and all its items were deleted, permanently
/// (FR-004, 014 research CS-11, CS-14).
/// </summary>
/// <remarks>Carries ids only. <c>Cms.CollectionDeleted</c> is a published contract name.</remarks>
public sealed record CollectionDeleted(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.CollectionDeleted";
}
