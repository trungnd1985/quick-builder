using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Sites.Contracts.Events;

namespace QuickBuilder.Identity.Features.RecordSiteSecurityEvents;

/// <summary>
/// Records site changes that alter what the public can reach in the security event
/// record (009 FR-028, research S-11).
/// </summary>
/// <remarks>
/// The Sites module cannot write Identity's tables, nor span both in one transaction
/// (constitution Principle I), so it publishes integration events and this records them.
/// An entry therefore appears a few seconds after the change, within the outbox poll
/// interval; the outbox guarantees it arrives.
/// <para>
/// <b>Deduplication is this handler's own.</b> The shared dispatcher marks an event
/// processed in the <em>publisher's</em> schema, in the Sites transaction; the security
/// event is written here, in Identity's. Those are two transactions, so a crash between
/// them would redeliver an event whose entry already exists. The contract for handlers
/// with effects outside the dispatcher's transaction applies (IIntegrationEventHandler):
/// each entry is saved together with a processed-event row in <em>Identity's own</em>
/// <c>processed_events</c>, keyed by the integration event's id, and a duplicate key means
/// the entry is already there.
/// </para>
/// <para>
/// The entry's time is when the change happened, not when it was delivered. It carries
/// no client address: the request that made the change is long over, and the actor is
/// what matters.
/// </para>
/// </remarks>
internal sealed class RecordSiteSecurityEventsHandler(IdentityDbContext database) :
    IIntegrationEventHandler<HostNameAdded>,
    IIntegrationEventHandler<HostNameRemoved>,
    IIntegrationEventHandler<SiteDisabled>,
    IIntegrationEventHandler<SiteEnabled>,
    IIntegrationEventHandler<SiteExportFinished>,
    IIntegrationEventHandler<SiteImportFinished>,
    IIntegrationEventHandler<SiteArchiveDownloaded>,
    IIntegrationEventHandler<SiteArchiveDeleted>
{
    public Task HandleAsync(HostNameAdded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent,
            SecurityEventTypes.SiteHostNameAdded,
            integrationEvent.ActorUserId,
            new SiteEventDetails(integrationEvent.SiteId, integrationEvent.HostName),
            cancellationToken);
    }

    public Task HandleAsync(HostNameRemoved integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent,
            SecurityEventTypes.SiteHostNameRemoved,
            integrationEvent.ActorUserId,
            new SiteEventDetails(integrationEvent.SiteId, integrationEvent.HostName),
            cancellationToken);
    }

    public Task HandleAsync(SiteDisabled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent,
            SecurityEventTypes.SiteDisabled,
            integrationEvent.ActorUserId,
            new SiteEventDetails(integrationEvent.SiteId, HostName: null),
            cancellationToken);
    }

    public Task HandleAsync(SiteEnabled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent,
            SecurityEventTypes.SiteEnabled,
            integrationEvent.ActorUserId,
            new SiteEventDetails(integrationEvent.SiteId, HostName: null),
            cancellationToken);
    }

    // 020 research ST-19: site export and import. The outcome decides the event type, so an
    // auditor filtering on site_imported sees only imports that committed.

    public Task HandleAsync(SiteExportFinished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var (type, outcome) = integrationEvent.Outcome switch
        {
            "succeeded" => (SecurityEventTypes.SiteExported, SecurityEventOutcomes.Succeeded),
            "cancelled" => (SecurityEventTypes.SiteTransferCancelled, SecurityEventOutcomes.Succeeded),
            _ => (SecurityEventTypes.SiteTransferFailed, SecurityEventOutcomes.Failed),
        };
        return RecordAsync(
            integrationEvent,
            type,
            integrationEvent.ActorUserId,
            new TransferEventDetails(integrationEvent.TransferId, integrationEvent.SiteId, Mode: null, integrationEvent.Trigger, integrationEvent.FailureCode, integrationEvent.Trigger == "cli"),
            cancellationToken,
            outcome);
    }

    public Task HandleAsync(SiteImportFinished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var (type, outcome) = integrationEvent.Outcome switch
        {
            "succeeded" when integrationEvent.Mode == "restore" => (SecurityEventTypes.SiteRestored, SecurityEventOutcomes.Succeeded),
            "succeeded" => (SecurityEventTypes.SiteImported, SecurityEventOutcomes.Succeeded),
            "cancelled" => (SecurityEventTypes.SiteTransferCancelled, SecurityEventOutcomes.Succeeded),
            _ => (SecurityEventTypes.SiteTransferFailed, SecurityEventOutcomes.Failed),
        };
        return RecordAsync(
            integrationEvent,
            type,
            integrationEvent.ActorUserId,
            new TransferEventDetails(integrationEvent.TransferId, integrationEvent.SiteId, integrationEvent.Mode, integrationEvent.Trigger, integrationEvent.FailureCode, integrationEvent.Trigger == "cli"),
            cancellationToken,
            outcome);
    }

    public Task HandleAsync(SiteArchiveDownloaded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent,
            SecurityEventTypes.SiteArchiveDownloaded,
            integrationEvent.ActorUserId,
            new TransferEventDetails(integrationEvent.TransferId, integrationEvent.SiteId, Mode: null, Trigger: null, FailureCode: null, Operator: false),
            cancellationToken);
    }

    public Task HandleAsync(SiteArchiveDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent,
            SecurityEventTypes.SiteArchiveDeleted,
            integrationEvent.ActorUserId,
            new TransferEventDetails(integrationEvent.TransferId, integrationEvent.SiteId, Mode: null, Trigger: null, FailureCode: null, Operator: false),
            cancellationToken);
    }

    private async Task RecordAsync(
        IIntegrationEvent integrationEvent,
        string eventType,
        Guid? actorUserId,
        SecurityEventDetails details,
        CancellationToken cancellationToken,
        string outcome = SecurityEventOutcomes.Succeeded)
    {
        var marker = new ProcessedEvent
        {
            EventId = integrationEvent.Id,
            HandlerName = $"{nameof(RecordSiteSecurityEventsHandler)}:{eventType}",
            ProcessedAtUtc = integrationEvent.OccurredAtUtc,
        };

        // Cheap check first, so an ordinary redelivery does not lean on an exception.
        var alreadyRecorded = await database.Set<ProcessedEvent>()
            .AsNoTracking()
            .AnyAsync(p => p.EventId == marker.EventId && p.HandlerName == marker.HandlerName, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyRecorded)
        {
            return;
        }

        database.Set<ProcessedEvent>().Add(marker);
        database.SecurityEvents.Add(SecurityEvent.Create(
            eventType,
            outcome,
            integrationEvent.OccurredAtUtc,
            actorUserId,
            subjectUserId: null,
            clientAddress: null,
            details));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Two deliveries raced past the check; the other one recorded it.
            database.ChangeTracker.Clear();
        }
    }
}
