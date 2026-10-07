namespace QuickBuilder.Cms.Domain.Events;

/// <summary>Something that happened to a Cms aggregate (research CS-14).</summary>
/// <remarks>
/// Each has exactly one integration event in <c>Cms.Contracts.Events</c>, added to the outbox
/// by <c>CmsOutbox</c>. They carry ids only: no names, keys or values.
/// </remarks>
internal interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}

internal sealed record CollectionCreatedEvent(Guid SiteId, Guid CollectionId, int Revision, DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record CollectionChangedEvent(
    Guid SiteId,
    Guid CollectionId,
    int Revision,
    bool SchemaChanged,
    bool ItemsRewritten,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record CollectionDeletedEvent(Guid SiteId, Guid CollectionId, DateTimeOffset OccurredAtUtc) : IDomainEvent;
