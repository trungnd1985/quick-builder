using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Deletes editing sessions that lapsed and takeover records older than 90 days, every minute
/// (023 research CE-03, CE-12).
/// </summary>
/// <remarks>
/// Housekeeping only: every read and every claim already ignores a lapsed session by comparing
/// its last sign of life with the clock, so a lock lapses on time whether or not this has run.
/// </remarks>
internal sealed partial class EditingSessionCleanupService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<EditingSessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanUpAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
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

    /// <summary>One sweep over every site. Exposed for tests.</summary>
    public async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EditorDbContext>();

        // Principle V: the one bypass of the site filter outside a request. Housekeeping runs
        // for every site at once and deletes only rows that are already unreachable: sessions
        // gone for the expiry period and takeovers past retention. EditingCleanupTests proves
        // it keeps live rows and recent records of every site.
        var lapsedBefore = now - EditingRules.Expiry;
        var sessions = await database.EditingSessions
            .AcrossAllSites("Housekeeping across every site: lapsed editing sessions (023 research CE-03).")
            .Where(s => s.LastSeenAt <= lapsedBefore)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var keptAfter = now - EditingRules.TakeoverRetention;
        var takeovers = await database.EditingTakeovers
            .AcrossAllSites("Housekeeping across every site: takeover records past retention (023 research CE-12).")
            .Where(t => t.TakenAt < keptAfter)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sessions + takeovers > 0)
        {
            LogSwept(logger, sessions, takeovers);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Editing cleanup deleted {Sessions} lapsed sessions and {Takeovers} old takeover records")]
    private static partial void LogSwept(ILogger logger, int sessions, int takeovers);

    [LoggerMessage(Level = LogLevel.Error, Message = "Editing cleanup failed; retrying next interval")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
