using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Cms.Contracts.Events;

// Collection items (015 research IT-17, module-surface.md §4). Each is written to the Cms outbox
// in the transaction of the change it announces. They carry ids, numbers and flags only, never
// values, names or slugs (FR-032). The event type names are published contract names. Templates
// and bindings (#16) subscribe to the three that change published data: ItemPublished,
// ItemUnpublished and ItemDeleted with WasPublished.

/// <summary>An item was created, including as a duplicate of another.</summary>
public sealed record ItemCreated(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    Guid ItemId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.ItemCreated";
}

/// <summary>An item's draft was saved, or replaced by a kept version. Published data did not change.</summary>
public sealed record ItemDraftSaved(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    Guid ItemId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.ItemDraftSaved";
}

/// <summary>
/// An item's published data changed to a new version: by a publish, a scheduled publish or a
/// rollback (<see cref="Kind"/>: <c>publish</c>, <c>scheduled</c>, <c>rollback</c>).
/// <see cref="SlugChanged"/> says whether its published slug, and so its address, changed.
/// </summary>
public sealed record ItemPublished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    Guid ItemId,
    int VersionNumber,
    string Kind,
    bool SlugChanged,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.ItemPublished";
}

/// <summary>An item's published data was removed, by hand or by a schedule.</summary>
public sealed record ItemUnpublished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    Guid ItemId,
    bool Scheduled,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.ItemUnpublished";
}

/// <summary>An item was deleted with its versions, schedules and history.</summary>
public sealed record ItemDeleted(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid CollectionId,
    Guid ItemId,
    bool WasPublished,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Cms.ItemDeleted";
}
