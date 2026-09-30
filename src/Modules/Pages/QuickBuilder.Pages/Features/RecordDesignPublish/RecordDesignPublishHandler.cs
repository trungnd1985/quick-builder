using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RecordDesignPublish;

/// <summary>
/// Records a design publish or rollback in the site's publish log (012 FR-017, research PR-16).
/// </summary>
/// <remarks>
/// The Editor module owns the design and cannot write this module's log, so it publishes
/// <see cref="DesignPublished"/> and this records it, within the outbox poll interval.
/// <para>
/// <b>Deduplication is this handler's own.</b> The entry is written in Pages' schema, outside
/// the dispatcher's transaction, so a redelivery must be harmless: the entry's id is the
/// event's id, and a duplicate key means it is already there.
/// </para>
/// </remarks>
internal sealed class RecordDesignPublishHandler(PagesDbContext database) : IIntegrationEventHandler<DesignPublished>
{
    public async Task HandleAsync(DesignPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var siteId = integrationEvent.SiteId;

        // Cheap check first, so an ordinary redelivery does not lean on an exception.
        if (await database.ForSite<PublishLogEntry>(siteId).AnyAsync(e => e.Id == integrationEvent.Id, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        database.PublishLog.Add(new PublishLogEntry(
            integrationEvent.Id,
            siteId,
            SubjectKinds.Design,
            siteId,
            subjectLabel: string.Empty,
            subjectPath: null,
            integrationEvent.Kind == VersionKinds.Rollback ? LogActions.Rollback : LogActions.Publish,
            integrationEvent.OccurredAtUtc)
        {
            VersionNumber = integrationEvent.VersionNumber,
            RestoredFromNumber = integrationEvent.RestoredFromNumber,
            ActorUserId = integrationEvent.ActorUserId,
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two deliveries raced past the check; the other one recorded it.
            database.ChangeTracker.Clear();
        }
    }
}
