using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Turns an aggregate's domain events into integration events in the Pages outbox, to be
/// saved with the change (001 FR-015, research P-14).
/// </summary>
/// <remarks>
/// Only adds: the caller's <c>SaveChanges</c> writes the events and the state change in one
/// transaction, so either both survive or neither does. The acting user is not part of the
/// domain events — who did something is a fact about the request, not about the tree — so
/// it is supplied here.
/// </remarks>
internal static class PagesOutbox
{
    internal static void AddFrom(PagesDbContext database, AggregateRoot aggregate, Guid? actorUserId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(aggregate);

        foreach (var domainEvent in aggregate.DomainEvents)
        {
            switch (domainEvent)
            {
                case PageCreatedEvent e:
                    Add(database, new PageCreated(Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.PageId, e.ParentId, e.Path, actorUserId));
                    break;
                case PageDeletedEvent e:
                    Add(database, new PageDeleted(Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.PageId, e.Path, actorUserId));
                    break;
                case PageAddressesChangedEvent e:
                    Add(database, new PageAddressesChanged(Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.Changes, actorUserId));
                    break;
                case HomePageChangedEvent e:
                    Add(database, new HomePageChanged(Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.PageId, e.PreviousPageId, actorUserId));
                    break;
                case ComponentCreatedEvent e:
                    Add(database, new ComponentCreated(Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.ComponentId, actorUserId));
                    break;
                case ComponentDeletedEvent e:
                    Add(database, new ComponentDeleted(Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.ComponentId, actorUserId));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"No integration event is defined for {domainEvent.GetType().Name}. Add one to Pages.Contracts.Events.");
            }
        }

        aggregate.ClearDomainEvents();
    }

    internal static void Add<TEvent>(PagesDbContext database, TEvent integrationEvent, DateTimeOffset? availableAtUtc = null)
        where TEvent : IIntegrationEvent
    {
        database.OutboxMessages.Add(OutboxMessage.From(integrationEvent, availableAtUtc));
    }
}
