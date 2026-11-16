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

/// <summary>021 TR-08: the item came out of the trash.</summary>
internal sealed record ItemRestoredEvent(Guid SiteId, Guid CollectionId, Guid ItemId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>017 FR-029, FR-032: a member accepted the redirect offer for the item's old address.</summary>
internal sealed record ItemRedirectRequestedEvent(
    Guid SiteId, Guid CollectionId, Guid ItemId, string OldSlug, string Reason, Guid? TargetPageId, DateTimeOffset OccurredAtUtc) : IDomainEvent;
