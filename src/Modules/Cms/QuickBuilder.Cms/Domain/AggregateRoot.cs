using QuickBuilder.Cms.Domain.Events;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// An aggregate root that records what happened to it (constitution Principle I: a rich
/// domain model with domain events for Cms).
/// </summary>
/// <remarks>
/// Events are recorded in memory while the aggregate changes, and turned into integration
/// events by <c>CmsOutbox.AddFrom</c> in the same <c>SaveChanges</c>. A change that is refused
/// raises nothing, because every method validates before it mutates. Internal to Cms, as
/// Pages keeps its own (Principle XI).
/// </remarks>
internal abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
