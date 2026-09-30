using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RefreshPagesShowingMedia;

/// <summary>
/// Refreshes the live pages that show a media file, directly or through a component, when the
/// file is deleted (012 data-model §3.2) or changes in a way pages show (013 research MV-17:
/// replaced, dimensions recorded, variants withdrawn), so they render the change at their
/// next request on every instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deduplication is this handler's own.</b> The dispatcher marks the event processed in
/// Media's schema; the generations move here, in Pages'. So, like
/// <c>RecordSiteSecurityEventsHandler</c> in Identity, the bump is saved together with a
/// processed-event row in Pages' own <c>processed_events</c>, keyed by the event's id and a
/// handler name per event type: a redelivery finds the row, and a duplicate key rolls the
/// second bump back.
/// </para>
/// <para>
/// Every <see cref="FileChanged"/> reason gets the same bump, including reasons added after
/// this was written: re-rendering a page that did not need it costs one render, while
/// missing one would leave visitors with stale images.
/// </para>
/// </remarks>
internal sealed class RefreshPagesShowingMediaHandler(IServiceScopeFactory scopes)
    : IIntegrationEventHandler<FileDeleted>, IIntegrationEventHandler<FileChanged>
{
    private const string DeletedHandlerName = nameof(RefreshPagesShowingMediaHandler);

    private const string ChangedHandlerName = nameof(RefreshPagesShowingMediaHandler) + ".FileChanged";

    public Task HandleAsync(FileDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.FileId, DeletedHandlerName, cancellationToken);
    }

    public Task HandleAsync(FileChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.FileId, ChangedHandlerName, cancellationToken);
    }

    private async Task RefreshAsync(
        Guid eventId, DateTimeOffset occurredAtUtc, Guid siteId, Guid fileId, string handlerName, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        var bump = scope.ServiceProvider.GetRequiredService<GenerationBump>();

        // Cheap check first, so an ordinary redelivery does not lean on an exception.
        var alreadyDone = await database.ProcessedEvents
            .AnyAsync(p => p.EventId == eventId && p.HandlerName == handlerName, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyDone)
        {
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await bump.BumpForTargetsAsync(siteId, [(DependencyKinds.Media, fileId)], exceptPageId: null, cancellationToken)
            .ConfigureAwait(false);

        database.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId,
            HandlerName = handlerName,
            ProcessedAtUtc = occurredAtUtc,
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two deliveries raced past the check; the other one's bump stands, this one's goes.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
