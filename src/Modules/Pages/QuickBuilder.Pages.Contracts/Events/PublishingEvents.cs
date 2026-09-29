using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Contracts.Events;

// 012 research PR-19, contracts/readers.md §3. Published in the same transaction as the change
// (001 FR-015). They carry ids, numbers and paths only — never titles, names or content
// (FR-044). The EventType names are published contracts: renaming one is a breaking change, so
// add a V2 alongside and retire the old one once nothing subscribes.

/// <summary>A page version became live: a publish, a scheduled publish or a rollback.</summary>
/// <param name="Kind"><c>publish</c>, <c>scheduled</c> or <c>rollback</c>.</param>
public sealed record PagePublished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid PageId,
    int VersionNumber,
    string Kind,
    string Path,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.PagePublished";
}

/// <summary>A page stopped being live.</summary>
public sealed record PageUnpublished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid PageId,
    string Path,
    bool Scheduled,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.PageUnpublished";
}

/// <summary>A component version became live: a publish, a scheduled publish or a rollback.</summary>
public sealed record ComponentPublished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid ComponentId,
    int VersionNumber,
    string Kind,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.ComponentPublished";
}

/// <summary>A component stopped being live.</summary>
public sealed record ComponentUnpublished(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid ComponentId,
    Guid? ActorUserId) : IIntegrationEvent
{
    public static string EventType => "Pages.ComponentUnpublished";
}
