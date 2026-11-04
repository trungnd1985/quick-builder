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
        where TEvent : IIntegrationEvent =>
        Add(database, PendingEvent.Of(integrationEvent));

    internal static void Add(SitesDbContext database, PendingEvent pending)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(pending);

        database.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            EventType = pending.EventType,
            Payload = pending.Payload,
            OccurredAtUtc = pending.OccurredAtUtc,
            AvailableAtUtc = pending.OccurredAtUtc,
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
        });
    }
}

/// <summary>
/// An event serialised with its contract name, so code that handles events of several types
/// can pass them around before they are added (020: transfer outcomes).
/// </summary>
internal sealed record PendingEvent(string EventType, string Payload, DateTimeOffset OccurredAtUtc)
{
    public static PendingEvent Of<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return new(TEvent.EventType, JsonSerializer.Serialize(integrationEvent), integrationEvent.OccurredAtUtc);
    }
}
