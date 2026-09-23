using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Shared.Infrastructure.Outbox;

/// <summary>Runs the handlers registered for one stored event, exactly once each.</summary>
public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(DbContext context, OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Maps a stored <c>event_type</c> string back to its CLR type.
/// </summary>
/// <remarks>
/// Registered by each publishing module so the dispatcher can deserialize a payload
/// without shared infrastructure knowing any module's event types by name.
/// </remarks>
public interface IEventTypeRegistration
{
    string EventType { get; }

    Type ClrType { get; }
}

/// <inheritdoc />
public sealed class EventTypeRegistration<TEvent> : IEventTypeRegistration
    where TEvent : IIntegrationEvent
{
    public string EventType => TEvent.EventType;

    public Type ClrType => typeof(TEvent);
}

/// <summary>
/// Turns at-least-once delivery into an effectively-once effect (FR-017, D-03).
/// </summary>
/// <remarks>
/// The deduplication is a unique constraint, not a check-then-act: the processed-events
/// row is inserted in the same transaction as the handler's effect, so there is no window
/// where the effect lands and the marker does not. A duplicate key means another pass
/// already did this work.
/// </remarks>
internal sealed class IntegrationEventDispatcher(
    IServiceProvider services,
    IEnumerable<IEventTypeRegistration> registrations) : IIntegrationEventDispatcher
{
    private readonly ConcurrentDictionary<string, Type> _eventTypes =
        new(registrations.ToDictionary(r => r.EventType, r => r.ClrType, StringComparer.Ordinal),
            StringComparer.Ordinal);

    public async Task DispatchAsync(DbContext context, OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        if (!_eventTypes.TryGetValue(message.EventType, out var eventType))
        {
            // Nothing knows how to read this. Treating it as delivered would lose it
            // silently; throwing puts it through the retry-then-fail path, where it ends
            // up visible in the failed state with a readable reason.
            throw new InvalidOperationException(
                $"No CLR type is registered for event type '{message.EventType}'. Register it " +
                "in the publishing module's Add<Module>Module() entry point.");
        }

        var payload = JsonSerializer.Deserialize(message.Payload, eventType)
            ?? throw new InvalidOperationException($"Event {message.Id} has an empty payload.");

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handlers = services.GetServices(handlerType).Where(h => h is not null).ToList();

        foreach (var handler in handlers)
        {
            var handlerName = handler!.GetType().FullName!;

            // FR-017: the marker and the effect commit together, or neither does.
            var alreadyProcessed = await context.Set<ProcessedEvent>()
                .AnyAsync(
                    p => p.EventId == message.Id && p.HandlerName == handlerName,
                    cancellationToken)
                .ConfigureAwait(false);

            if (alreadyProcessed)
            {
                continue;
            }

            await InvokeHandlerAsync(handlerType, handler, payload, cancellationToken).ConfigureAwait(false);

            context.Set<ProcessedEvent>().Add(new ProcessedEvent
            {
                EventId = message.Id,
                HandlerName = handlerName,
                ProcessedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private static Task InvokeHandlerAsync(
        Type handlerType,
        object handler,
        object payload,
        CancellationToken cancellationToken)
    {
        // Named by string rather than nameof(...): IIntegrationEvent has a static abstract
        // member, so it cannot itself be used as a type argument.
        var method = handlerType.GetMethod("HandleAsync")
            ?? throw new InvalidOperationException($"{handlerType} has no HandleAsync method.");

        return (Task)method.Invoke(handler, [payload, cancellationToken])!;
    }
}
