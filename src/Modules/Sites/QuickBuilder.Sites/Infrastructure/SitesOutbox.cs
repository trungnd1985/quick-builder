using System.Text.Json;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// Adds an integration event to the Sites outbox, to be saved with the change (FR-015).
/// </summary>
/// <remarks>
/// Only adds: the caller's <c>SaveChanges</c> writes the event and the state change in one
/// transaction, so either both survive or neither does.
/// </remarks>
internal static class SitesOutbox
{
    internal static void Add<TEvent>(SitesDbContext database, TEvent integrationEvent)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        database.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            EventType = TEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent),
            OccurredAtUtc = integrationEvent.OccurredAtUtc,
            AvailableAtUtc = integrationEvent.OccurredAtUtc,
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
        });
    }
}
