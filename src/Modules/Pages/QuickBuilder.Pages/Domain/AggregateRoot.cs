using QuickBuilder.Pages.Domain.Events;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// An aggregate root that records what happened to it (constitution Principle I: a rich
/// domain model with domain events for Pages).
/// </summary>
/// <remarks>
/// Events are recorded in memory while the aggregate changes, and turned into integration
/// events by <c>PagesOutbox.AddFrom</c> in the same <c>SaveChanges</c> (research P-14). A
/// change that is refused raises nothing, because every method validates before it
/// mutates. This stays internal to Pages: no other module needs a domain-event mechanism
/// yet, so none goes into the kernel (Principle XI).
/// </remarks>
internal abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
