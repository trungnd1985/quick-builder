using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// Deletes archives that are no longer needed, and old job rows (020 research ST-16; FR-008).
/// </summary>
/// <remarks>
/// Hourly. An export's archive goes 7 days after the export finished, an upload 24 hours after
/// it arrived unless an import confirmed it, and an import's upload as soon as the import
/// committed. Job rows go 90 days after they finished. A job whose archive is in use by a
/// worker is never touched: it holds a lease. Storage deletion is idempotent, so a crash
/// between deleting the bytes and saving the job only repeats the deletion next hour.
/// </remarks>
internal sealed class TransferRetentionService(
    IServiceScopeFactory scopes,
    IFileStorage storage,
    TimeProvider clock,
    ILogger<TransferRetentionService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The site transfer retention sweep failed; retrying next hour.");
            }
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Deletes expired archives and old rows. Returns how many archives it deleted. Exposed for tests.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var deleted = 0;

        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
            var due = await database.TransferJobs
                .Where(j => j.ArchiveKey != null && j.ArchiveExpiresAtUtc <= now
                    && j.State != TransferStates.Checking && j.State != TransferStates.Running && j.State != TransferStates.Confirmed)
                .OrderBy(j => j.ArchiveExpiresAtUtc)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (due.Count == 0)
            {
                break;
            }

            foreach (var job in due)
            {
                await storage.DeleteAsync(new StorageKey(job.ArchiveKey!), cancellationToken).ConfigureAwait(false);
                job.ArchiveDeleted(now);
                if (await store.SaveAsync(job, [PendingEvent.Of(new SiteArchiveDeleted(Guid.CreateVersion7(), now, job.Id, job.SiteId, ActorUserId: null))], cancellationToken)
                    .ConfigureAwait(false) == SaveOutcome.Saved)
                {
                    deleted++;
                }
            }

            if (due.Count < BatchSize)
            {
                break;
            }
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            // An upload whose bytes never arrived holds no archive, but ends like an expired one.
            var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
            var abandonedBefore = now - TransferJob.UploadRetention;
            var abandoned = await scope.ServiceProvider.GetRequiredService<SitesDbContext>().TransferJobs
                .Where(j => j.State == TransferStates.Uploading && j.CreatedAtUtc < abandonedBefore)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var job in abandoned)
            {
                job.ArchiveDeleted(now);
                await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var cutoff = now - TransferJob.RowRetention;
            var removed = await scope.ServiceProvider.GetRequiredService<SitesDbContext>().TransferJobs
                .Where(j => j.FinishedAtUtc < cutoff && j.ArchiveKey == null)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            if (removed > 0)
            {
                logger.LogInformation("Removed {Count} site transfer record(s) older than 90 days.", removed);
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} expired site archive(s).", deleted);
        }

        return deleted;
    }
}
