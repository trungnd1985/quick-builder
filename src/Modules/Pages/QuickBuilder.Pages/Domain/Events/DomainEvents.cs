using QuickBuilder.Pages.Contracts.Events;

namespace QuickBuilder.Pages.Domain.Events;

/// <summary>Something that happened to a Pages aggregate (research P-14).</summary>
/// <remarks>
/// Each has exactly one integration event in <c>Pages.Contracts.Events</c>, added to the
/// outbox by <c>PagesOutbox</c>. They carry ids and paths only (FR-037).
/// </remarks>
internal interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}

internal sealed record PageCreatedEvent(Guid SiteId, Guid PageId, Guid? ParentId, string Path, DateTimeOffset OccurredAtUtc)
    : IDomainEvent;

internal sealed record PageDeletedEvent(Guid SiteId, Guid PageId, string Path, DateTimeOffset OccurredAtUtc)
    : IDomainEvent;

/// <summary>Every address one operation changed, old and new (P-14).</summary>
internal sealed record PageAddressesChangedEvent(
    Guid SiteId,
    IReadOnlyList<PageAddressChange> Changes,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record HomePageChangedEvent(Guid SiteId, Guid PageId, Guid? PreviousPageId, DateTimeOffset OccurredAtUtc)
    : IDomainEvent;

/// <summary>A page became a collection's template, changed collection, or stopped being one (016 FR-036).</summary>
internal sealed record PageTemplateChangedEvent(
    Guid SiteId, Guid PageId, Guid? CollectionId, Guid? PreviousCollectionId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record ComponentCreatedEvent(Guid SiteId, Guid ComponentId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

internal sealed record ComponentDeletedEvent(Guid SiteId, Guid ComponentId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>A page or component version became live (012 research PR-19).</summary>
internal sealed record SubjectPublishedEvent(
    Guid SiteId,
    string SubjectKind,
    Guid SubjectId,
    int VersionNumber,
    string Kind,
    string? Path,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;

/// <summary>A page or component stopped being live (012 research PR-19).</summary>
internal sealed record SubjectUnpublishedEvent(
    Guid SiteId,
    string SubjectKind,
    Guid SubjectId,
    bool Scheduled,
    string? Path,
    DateTimeOffset OccurredAtUtc) : IDomainEvent;
