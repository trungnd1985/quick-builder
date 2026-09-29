namespace QuickBuilder.Shared.Infrastructure.Outbox;

/// <summary>
/// An integration event awaiting delivery, stored in its own module's schema (D-01).
/// </summary>
/// <remarks>
/// Per-module rather than one shared table: the row is written on the module's own
/// DbContext, so the event and the state change that caused it are a single SaveChanges
/// in a single transaction (FR-015). No module writes outside its own schema (FR-022) and
/// no transaction spans two modules (FR-021).
/// </remarks>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Contract name, e.g. <c>Sites.SiteCreated</c>.</summary>
    public required string EventType { get; set; }

    /// <summary>Serialized event body (jsonb).</summary>
    public required string Payload { get; set; }

    /// <summary>When the state change happened, not when it was dispatched.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>
    /// Next eligible attempt. Equals <see cref="OccurredAtUtc"/> initially and is pushed
    /// out by backoff after each failure.
    /// </summary>
    public DateTimeOffset AvailableAtUtc { get; set; }

    public int Attempts { get; set; }

    /// <summary>Set on success. The retention sweep keys on this.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Set when attempts are exhausted. Terminal — nothing retries it (FR-018).</summary>
    public DateTimeOffset? FailedAtUtc { get; set; }

    /// <summary>
    /// Exception type and message only. Never the payload — that could carry content
    /// bodies or personal data (FR-032).
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Trace id of the request that produced this event, so dispatch correlates back to
    /// the operation that caused it (D-10).
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// The stored form of <paramref name="integrationEvent"/>, available at once or, for a
    /// delayed message, not before <paramref name="availableAtUtc"/>.
    /// </summary>
    /// <remarks>
    /// A delayed message is how a scheduled publish waits for its time (012 research PR-14):
    /// the dispatcher does not claim a row before <see cref="AvailableAtUtc"/>, so the outbox's
    /// claiming, retries and restart safety carry the schedule with no second scheduler.
    /// </remarks>
    public static OutboxMessage From<TEvent>(TEvent integrationEvent, DateTimeOffset? availableAtUtc = null)
        where TEvent : Kernel.Events.IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            EventType = TEvent.EventType,
            Payload = System.Text.Json.JsonSerializer.Serialize(integrationEvent),
            OccurredAtUtc = integrationEvent.OccurredAtUtc,
            AvailableAtUtc = availableAtUtc ?? integrationEvent.OccurredAtUtc,
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
        };
    }
}

/// <summary>
/// Records that one handler has processed one event (FR-017, D-03).
/// </summary>
/// <remarks>
/// Written in the same transaction as the handler's effect, so there is no window where
/// the effect lands and the marker does not. A duplicate key on insert means "already
/// done": the transaction rolls back and the message is completed without re-running the
/// effect. This is what turns at-least-once delivery into an effectively-once effect.
/// </remarks>
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }

    public required string HandlerName { get; set; }

    public DateTimeOffset ProcessedAtUtc { get; set; }
}
