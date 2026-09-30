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
/// Refreshes the live pages that showed a deleted media file, directly or through a
/// component (012 data-model §3.2), so they stop showing it at their next request.
/// </summary>
/// <remarks>
/// <b>Deduplication is this handler's own.</b> The dispatcher marks the event processed in
/// Media's schema; the generations move here, in Pages'. So, like
/// <c>RecordSiteSecurityEventsHandler</c> in Identity, the bump is saved together with a
/// processed-event row in Pages' own <c>processed_events</c>, keyed by the event's id: a
/// redelivery finds the row, and a duplicate key rolls the second bump back.
/// </remarks>
internal sealed class RefreshPagesShowingMediaHandler(IServiceScopeFactory scopes) : IIntegrationEventHandler<FileDeleted>
{
    private const string HandlerName = nameof(RefreshPagesShowingMediaHandler);

    public async Task HandleAsync(FileDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        var bump = scope.ServiceProvider.GetRequiredService<GenerationBump>();

        // Cheap check first, so an ordinary redelivery does not lean on an exception.
        var alreadyDone = await database.ProcessedEvents
            .AnyAsync(p => p.EventId == integrationEvent.Id && p.HandlerName == HandlerName, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyDone)
        {
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await bump.BumpForTargetsAsync(
                integrationEvent.SiteId, [(DependencyKinds.Media, integrationEvent.FileId)], exceptPageId: null, cancellationToken)
            .ConfigureAwait(false);

        database.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = integrationEvent.Id,
            HandlerName = HandlerName,
            ProcessedAtUtc = integrationEvent.OccurredAtUtc,
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
