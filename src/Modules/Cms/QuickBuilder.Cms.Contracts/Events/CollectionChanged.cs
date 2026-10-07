using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Cms.Contracts.Events;

/// <summary>
/// A save gave a collection a new revision (FR-034, 014 research CS-14). Published in the
/// same transaction as the change.
/// </summary>
/// <remarks>
/// <see cref="SchemaChanged"/> is false when only the collection's name, address name or
/// description changed. <see cref="ItemsRewritten"/> says whether any stored item value was
/// rewritten, draft or published; templates and bindings (#16) refresh live pages from it.
/// Carries ids only. <c>Cms.CollectionChanged</c> is a published contract name.
/// </remarks>
public sealed record CollectionChanged(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    int Revision,
    bool SchemaChanged,
    bool ItemsRewritten,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.CollectionChanged";
}
