using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Domain.Redirects;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Saves the redirect use counted in memory every minute and when the instance stops (017
/// FR-021a, research SE-12).
/// </summary>
/// <remarks>
/// Counts are added to <c>pages.redirect_usage</c>, never replaced, so several instances add up.
/// The last-used time keeps the latest of what is stored and what was counted. A redirect
/// deleted since its use was counted is skipped. This is the only writer of the table, and it
/// runs on the module's own connection, never on the public path's.
/// </remarks>
internal sealed partial class RedirectUsageFlusher(
    RedirectHitCounter counter,
    IServiceScopeFactory scopes,
    ILogger<RedirectUsageFlusher> logger) : BackgroundService
{
    /// <summary>At most this long between flushes (FR-021a allows five minutes).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await FlushSafelyAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: StopAsync saves what is left.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await FlushSafelyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saves the counts gathered so far. Tests call it to flush at once.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var tallies = counter.Drain();
        if (tallies.Count == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PagesDbContext>();

        foreach (var tally in tallies)
        {
            var updated = await Usage(database, tally)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(u => u.Hits, u => u.Hits + tally.Hits)
                        .SetProperty(u => u.LastUsedAtUtc, u => u.LastUsedAtUtc > tally.LastUsedAtUtc ? u.LastUsedAtUtc : tally.LastUsedAtUtc),
                    cancellationToken)
                .ConfigureAwait(false);
            if (updated > 0)
            {
                continue;
            }

            var exists = await database.ForSite<Redirect>(tally.SiteId)
                .AnyAsync(r => r.Id == tally.RedirectId, cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                continue;
            }

            database.RedirectUsages.Add(new RedirectUsage(tally.RedirectId, tally.SiteId, tally.Hits, tally.LastUsedAtUtc));
            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Another instance inserted the row first, or the redirect went: add to theirs.
                database.ChangeTracker.Clear();
                await Usage(database, tally)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Hits, u => u.Hits + tally.Hits), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static IQueryable<RedirectUsage> Usage(PagesDbContext database, RedirectHitTally tally) =>
        database.ForSite<RedirectUsage>(tally.SiteId).Where(u => u.RedirectId == tally.RedirectId);

    private async Task FlushSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Counts are a statistic: losing one flush must never stop the next (FR-021a).
            LogFlushFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving redirect usage failed; the counts of this flush are lost.")]
    private static partial void LogFlushFailed(ILogger logger, Exception exception);
}
