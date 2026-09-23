using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Infrastructure.Modules;

namespace QuickBuilder.Shared.Infrastructure.Outbox;

/// <summary>Settings the dispatcher reads (contracts/integration-events.md).</summary>
public sealed record OutboxSettings(
    bool Enabled,
    TimeSpan PollInterval,
    int BatchSize,
    TimeSpan HandlerTimeout,
    int MaxAttempts,
    TimeSpan RetentionPeriod);

/// <summary>
/// Delivers stored integration events at least once (FR-016, FR-018, FR-019, D-02).
/// </summary>
/// <remarks>
/// One pass per module per tick:
/// <list type="number">
/// <item>claim a batch with <c>FOR UPDATE SKIP LOCKED</c> inside a transaction;</item>
/// <item>run each handler, writing its processed-events marker in the same transaction
/// (FR-017);</item>
/// <item>mark the message completed, or back it off, or fail it terminally.</item>
/// </list>
/// <para>
/// The row lock is the whole concurrency story (D-02). PostgreSQL holds it for the life
/// of the transaction, so a second instance skips claimed rows, and an instance that dies
/// has its transaction rolled back by the server — the row becomes eligible again with no
/// lease column, no clock comparison and no stuck-claim sweeper to write and test.
/// </para>
/// <para>
/// The cost of that simplicity: a handler holds a transaction for its whole duration.
/// Handlers must be short, which the handler timeout enforces.
/// </para>
/// </remarks>
public sealed class OutboxDispatcher(
    IServiceProvider services,
    IOutboxSettingsProvider settingsProvider,
    IIntegrationEventDispatcher eventDispatcher,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = settingsProvider.Get();

        if (!settings.Enabled)
        {
            logger.LogInformation("Outbox dispatch is disabled (Outbox:Enabled=false).");
            return;
        }

        logger.LogInformation(
            "Outbox dispatcher started: every {Interval}, up to {BatchSize} per pass.",
            settings.PollInterval,
            settings.BatchSize);

        using var timer = new PeriodicTimer(settings.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed pass must never kill the dispatcher: the next tick retries, and
                // the events are still in the table. Crashing here would stop delivery for
                // the whole process over one bad batch.
                logger.LogError(ex, "Outbox dispatch pass failed; retrying next tick.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One dispatch pass across every registered module. Exposed for tests.</summary>
    public async Task<int> RunOnceAsync(OutboxSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var dispatched = 0;

        using var scope = services.CreateScope();
        foreach (var provider in scope.ServiceProvider.GetServices<IModuleDatabaseProvider>())
        {
            dispatched += await DispatchModuleAsync(provider, settings, cancellationToken).ConfigureAwait(false);
        }

        return dispatched;
    }

    private async Task<int> DispatchModuleAsync(
        IModuleDatabaseProvider provider,
        OutboxSettings settings,
        CancellationToken cancellationToken)
    {
        var description = provider.Describe();
        var dispatched = 0;

        using var scope = services.CreateScope();
        await using var context = provider.CreateContext(scope.ServiceProvider);

        // Everything below — claim, handle, complete — is one transaction. That is what
        // makes the effect and its processed-events marker land together (FR-017).
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // A schema name is an identifier, so it cannot be a query parameter. It comes from
        // the module's own registration rather than from any request, and is validated
        // against a strict identifier pattern before reaching here — see
        // ModuleDatabase.Schema. The batch size IS parameterised.
        //
        // Constitution: raw SQL is allowed only through EF Core, inside the module that
        // owns the tables, parameterised. The site filter does not apply — outbox rows are
        // module-owned, not site-owned.
        // Concatenated rather than interpolated so that {0} stays a literal placeholder
        // for FromSqlRaw's parameter rather than being consumed by string interpolation.
        var schema = SchemaIdentifier.Validate(description.Schema);
        var sql =
            "SELECT * FROM \"" + schema + "\".outbox_messages " +
            "WHERE completed_at_utc IS NULL " +
            "  AND failed_at_utc IS NULL " +
            "  AND available_at_utc <= NOW() " +
            "ORDER BY available_at_utc " +
            "LIMIT {0} " +
            "FOR UPDATE SKIP LOCKED";

#pragma warning disable EF1002 // Schema is a validated identifier; the only value is parameterised.
        var claimed = await context.Set<OutboxMessage>()
            .FromSqlRaw(sql, settings.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002

        if (claimed.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        foreach (var message in claimed)
        {
            await HandleOneAsync(context, message, settings, cancellationToken).ConfigureAwait(false);
            dispatched++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return dispatched;
    }

    private async Task HandleOneAsync(
        DbContext context,
        OutboxMessage message,
        OutboxSettings settings,
        CancellationToken cancellationToken)
    {
        // D-10: background work gets its own activity, and it carries the trace id of the
        // request that produced the event — so a log line here ties back to the operation
        // that caused it, not just to "the dispatcher".
        using var activity = Telemetry.Source.StartActivity("outbox.dispatch");
        activity?.SetTag("outbox.event_type", message.EventType);
        activity?.SetTag("outbox.origin_trace_id", message.TraceId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.HandlerTimeout);

        try
        {
            await eventDispatcher
                .DispatchAsync(context, message, timeout.Token)
                .ConfigureAwait(false);

            message.CompletedAtUtc = DateTimeOffset.UtcNow;

            logger.LogInformation(
                "Delivered {EventType} ({EventId}).", message.EventType, message.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || timeout.IsCancellationRequested)
        {
            message.Attempts++;
            // Message and type only — never the payload, which could carry content bodies
            // or personal data (FR-032).
            message.LastError = $"{ex.GetType().Name}: {ex.Message}";

            if (message.Attempts >= settings.MaxAttempts)
            {
                message.FailedAtUtc = DateTimeOffset.UtcNow;

                // Terminal. No automatic requeue exists in this feature, deliberately —
                // no spec needs one yet (Principle XI). This log line is how an operator
                // finds it.
                logger.LogError(
                    ex,
                    "Event {EventType} ({EventId}) failed permanently after {Attempts} attempts.",
                    message.EventType,
                    message.Id,
                    message.Attempts);
            }
            else
            {
                // Exponential backoff, so a transient failure does not spin.
                var delay = TimeSpan.FromSeconds(Math.Pow(2, message.Attempts));
                message.AvailableAtUtc = DateTimeOffset.UtcNow + delay;

                logger.LogWarning(
                    "Event {EventType} ({EventId}) failed on attempt {Attempts}; retrying in {Delay}.",
                    message.EventType,
                    message.Id,
                    message.Attempts,
                    delay);
            }
        }
    }
}

/// <summary>
/// Supplies outbox settings without shared infrastructure depending on the host's options
/// types.
/// </summary>
public interface IOutboxSettingsProvider
{
    OutboxSettings Get();
}

/// <summary>Guards the one identifier that reaches raw SQL.</summary>
internal static class SchemaIdentifier
{
    /// <summary>
    /// Rejects anything that is not a plain lower-case identifier.
    /// </summary>
    /// <remarks>
    /// Schema names come from module registration, not from user input, so this is
    /// belt-and-braces rather than a live threat. It exists so that a future module
    /// computing its schema name from configuration cannot quietly turn a trusted
    /// identifier into an injection point.
    /// </remarks>
    internal static string Validate(string schema)
    {
        if (string.IsNullOrWhiteSpace(schema)
            || !schema.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException(
                $"'{schema}' is not a valid schema identifier. Module schemas must be " +
                "lower-case letters, digits and underscores.",
                nameof(schema));
        }

        return schema;
    }
}
