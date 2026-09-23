namespace QuickBuilder.Shared.Kernel.Events;

/// <summary>
/// Something that happened in one module which other modules may react to.
/// </summary>
/// <remarks>
/// Persisted to the publishing module's outbox in the same transaction as the state
/// change it describes (FR-015), then delivered asynchronously at least once (FR-016).
/// <para>
/// <see cref="EventType"/> is a published contract name in the form
/// <c>&lt;Module&gt;.&lt;EventName&gt;</c>. Renaming one is a breaking change: introduce
/// a V2 alongside and retire the old name once nothing subscribes to it.
/// </para>
/// <para>
/// Payloads carry identifiers and the few fields a handler needs — not whole aggregates.
/// A handler needing more reads through the owning module's Contracts project.
/// </para>
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>Stable across redeliveries. This is what idempotency keys on.</summary>
    Guid Id { get; }

    /// <summary>When the state change happened, not when it was dispatched.</summary>
    DateTimeOffset OccurredAtUtc { get; }

    /// <summary>Contract name, e.g. <c>Sites.SiteCreated</c>.</summary>
    static abstract string EventType { get; }
}

/// <summary>
/// Handles one integration event.
/// </summary>
/// <remarks>
/// Handlers MUST be idempotent (FR-017). The dispatcher writes a processed-events row in
/// the same transaction as the handler's effect, which covers anything written through
/// the same DbContext; a handler causing effects outside that transaction is responsible
/// for its own deduplication and must say so in a comment.
/// <para>
/// Handlers MUST be short. They run inside the claim transaction (D-02), holding a row
/// lock and a connection for their duration.
/// </para>
/// <para>
/// Delivery order is NOT guaranteed. Events are claimed in availability order but
/// processed concurrently and retried independently.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name is fixed by contracts/integration-events.md and is the " +
                    "conventional term in this architecture. This is not a .NET event " +
                    "handler delegate, and the rule's concern does not apply.")]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
