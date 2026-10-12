using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Runs a generation bump for another module's event exactly once (012 data-model §3.2).
/// </summary>
/// <remarks>
/// The dispatcher marks the event processed in the sending module's schema; the generations
/// move here, in Pages'. So the bump is saved together with a row in Pages' own
/// <c>processed_events</c>, keyed by the event's id and a handler name per event type: a
/// redelivery finds the row, and a duplicate key rolls the second bump back.
/// </remarks>
internal static class RefreshOnce
{
    public static async Task RunAsync(
        IServiceScopeFactory scopes,
        Guid eventId,
        DateTimeOffset occurredAtUtc,
        string handlerName,
        Func<GenerationBump, CancellationToken, Task> refresh,
        CancellationToken cancellationToken)
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

        await refresh(bump, cancellationToken).ConfigureAwait(false);

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
