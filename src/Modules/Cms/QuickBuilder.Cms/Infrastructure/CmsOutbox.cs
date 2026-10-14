using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Domain.Events;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Turns an aggregate's domain events into integration events in the Cms outbox, to be saved
/// with the change (001 FR-015, 014 research CS-14).
/// </summary>
/// <remarks>
/// Only adds: the caller's <c>SaveChanges</c> writes the events and the state change in one
/// transaction, so either both survive or neither does. The acting user is a fact about the
/// request, so it is supplied here, as <c>PagesOutbox</c> does.
/// </remarks>
internal static class CmsOutbox
{
    internal static void AddFrom(CmsDbContext database, AggregateRoot aggregate, Guid? actorUserId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(aggregate);

        foreach (var domainEvent in aggregate.DomainEvents)
        {
            var message = domainEvent switch
            {
                CollectionCreatedEvent e => OutboxMessage.From(new CollectionCreated(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.Revision, actorUserId)),
                CollectionChangedEvent e => OutboxMessage.From(new CollectionChanged(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.Revision, e.SchemaChanged, e.ItemsRewritten, actorUserId)),
                CollectionDeletedEvent e => OutboxMessage.From(new CollectionDeleted(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, actorUserId)),
                ItemCreatedEvent e => OutboxMessage.From(new ItemCreated(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.ItemId, actorUserId)),
                ItemDraftSavedEvent e => OutboxMessage.From(new ItemDraftSaved(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.ItemId, actorUserId)),
                ItemPublishedEvent e => OutboxMessage.From(new ItemPublished(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.ItemId, e.VersionNumber, e.Kind, e.SlugChanged, actorUserId)),
                ItemUnpublishedEvent e => OutboxMessage.From(new ItemUnpublished(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.ItemId, e.Scheduled, actorUserId)),
                ItemDeletedEvent e => OutboxMessage.From(new ItemDeleted(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.ItemId, e.WasPublished, actorUserId)),
                ItemRedirectRequestedEvent e => OutboxMessage.From(new ItemRedirectRequested(
                    Guid.CreateVersion7(), e.OccurredAtUtc, e.SiteId, e.CollectionId, e.ItemId, e.OldSlug, e.Reason, e.TargetPageId, 301, actorUserId)),
                _ => throw new InvalidOperationException(
                    $"No integration event is defined for {domainEvent.GetType().Name}. Add one to Cms.Contracts.Events."),
            };

            database.OutboxMessages.Add(message);
        }

        aggregate.ClearDomainEvents();
    }
}
