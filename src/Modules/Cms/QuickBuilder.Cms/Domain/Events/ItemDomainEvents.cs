namespace QuickBuilder.Cms.Domain.Events;

// What happened to an item (015 research IT-17). Ids, numbers and flags only: no values, names
// or slugs. Each becomes one integration event in Cms.Contracts.Events through CmsOutbox.

internal sealed record ItemCreatedEvent(Guid SiteId, Guid CollectionId, Guid ItemId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record ItemDraftSavedEvent(Guid SiteId, Guid CollectionId, Guid ItemId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record ItemPublishedEvent(
    Guid SiteId,
    Guid CollectionId,
    Guid ItemId,
    int VersionNumber,
    string Kind,
    bool SlugChanged,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record ItemUnpublishedEvent(Guid SiteId, Guid CollectionId, Guid ItemId, bool Scheduled, DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record ItemDeletedEvent(Guid SiteId, Guid CollectionId, Guid ItemId, bool WasPublished, DateTimeOffset OccurredAtUtc) : IDomainEvent;
