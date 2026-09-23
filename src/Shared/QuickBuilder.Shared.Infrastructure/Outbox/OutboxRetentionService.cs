using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Infrastructure.Modules;

namespace QuickBuilder.Shared.Infrastructure.Outbox;

/// <summary>
/// Removes delivered events once they are older than the retention window (FR-020).
/// </summary>
/// <remarks>
/// Without this the outbox grows forever. The partial index on pending rows means query
/// performance would not degrade, but the table and its backups would, and an operator
/// would eventually be asked to explain why the database is mostly history.
/// <para>
/// Only completed rows are swept. Failed rows are kept indefinitely on purpose: they are
/// the ones somebody needs to look at, and deleting evidence of a failure to save disk is
/// a poor trade.
/// </para>
/// </remarks>
public sealed class OutboxRetentionService(
    IServiceProvider services,
    IOutboxSettingsProvider settingsProvider,
    ILogger<OutboxRetentionService> logger) : BackgroundService
{
    // Hourly: the retention window is measured in days, so there is nothing to gain from
    // sweeping more often, and a cheap query on a schedule beats a clever one.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = settingsProvider.Get();

        if (!settings.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(settings.RetentionPeriod, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox retention sweep failed; retrying next interval.");
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

    /// <summary>Deletes completed events older than the window. Exposed for tests.</summary>
    public async Task<int> SweepAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - retentionPeriod;
        var removed = 0;

        using var scope = services.CreateScope();
        foreach (var provider in scope.ServiceProvider.GetServices<IModuleDatabaseProvider>())
        {
            await using var context = provider.CreateContext(scope.ServiceProvider);

            removed += await context.Set<OutboxMessage>()
                .Where(m => m.CompletedAtUtc != null && m.CompletedAtUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} delivered outbox event(s) older than {Retention}.",
                removed, retentionPeriod);
        }

        return removed;
    }
}
