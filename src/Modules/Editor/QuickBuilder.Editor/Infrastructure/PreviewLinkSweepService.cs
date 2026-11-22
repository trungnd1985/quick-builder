using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Deletes preview links that ended more than 30 days ago and failure windows older than an
/// hour, every hour (022 research PV-19).
/// </summary>
/// <remarks>
/// Housekeeping only: a link stops working at its expiry or revocation whether or not this has
/// run, because every request checks it (PV-08). Each pass is a conditional delete, so several
/// instances running it at once delete each row once. With the 30-day maximum lifetime, no link
/// row is older than 60 days.
/// </remarks>
internal sealed partial class PreviewLinkSweepService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<PreviewLinkSweepService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    /// <summary>How long an ended link is kept, so its creator still sees it was revoked or expired.</summary>
    public static readonly TimeSpan KeptAfterEnd = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
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
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<EditorDbContext>();

        // Principle V: housekeeping across every site, deleting only links that already work
        // for no one. PreviewLinkSweepTests proves active links of every site are kept.
        var endedBefore = now - KeptAfterEnd;
        var links = await database.PreviewLinks
            .AcrossAllSites("Housekeeping across every site: preview links ended long ago (022 research PV-19).")
            .Where(l => l.ExpiresAtUtc < endedBefore || (l.RevokedAtUtc != null && l.RevokedAtUtc < endedBefore))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Windows are installation data; an hour window ends an hour after it starts.
        var windowsBefore = now - TimeSpan.FromHours(2);
        var windows = await database.PreviewLinkFailures
            .Where(w => w.WindowStartUtc < windowsBefore)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (links + windows > 0)
        {
            LogSwept(logger, links, windows);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Preview link sweep deleted {Links} ended links and {Windows} old failure windows")]
    private static partial void LogSwept(ILogger logger, int links, int windows);

    [LoggerMessage(Level = LogLevel.Error, Message = "Preview link sweep failed; retrying next interval")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
