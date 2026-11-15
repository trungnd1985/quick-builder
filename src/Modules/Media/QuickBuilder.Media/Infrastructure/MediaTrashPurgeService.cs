using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Deletes trashed media files permanently, with their bytes, once they have been in the trash longer
/// than <c>Trash:RetentionDays</c> (021 US6, FR-024, research TR-13).
/// </summary>
/// <remarks>
/// Hourly, in batches of 100, every entry in its own transaction through <see cref="MediaTrashPurge"/>,
/// recorded as made by the system. Several instances may run it at once: the purge deletes a row
/// only while it is still in the trash and due, so each entry is purged once, and an entry whose
/// purge fails stays restorable and is tried again next hour.
/// </remarks>
internal sealed class MediaTrashPurgeService(
    IServiceScopeFactory scopes,
    TrashRetention retention,
    TimeProvider clock,
    ILogger<MediaTrashPurgeService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int BatchSize = 100;
    private const string Reason = "The retention job reads every site's trash; each entry is purged in its own site (021 TR-13).";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await PurgeDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The trash purge of media files failed; retrying next hour.");
            }
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Purges every due entry; returns how many were purged. Public for tests.</summary>
    public async Task<int> PurgeDueAsync(CancellationToken cancellationToken)
    {
        var due = retention.DueBefore(clock.GetUtcNow());
        var tried = new HashSet<Guid>();
        var purged = 0;

        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            var purge = scope.ServiceProvider.GetRequiredService<MediaTrashPurge>();

            var batch = await database.MediaFiles.AcrossAllSites(Reason).OnlyTrash(Reason)
                .Where(i => i.TrashedAtUtc <= due && !tried.Contains(i.Id))
                .OrderBy(i => i.TrashedAtUtc)
                .Take(BatchSize)
                .Select(i => new { i.SiteId, i.Id })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var entry in batch)
            {
                tried.Add(entry.Id);
                try
                {
                    // Another instance may have purged it, or a member restored it: nothing to do.
                    if ((await purge.PurgeAsync(entry.SiteId, entry.Id, actor: null, due, cancellationToken).ConfigureAwait(false)).IsSuccess)
                    {
                        purged++;
                    }
                }
                catch (DbUpdateException ex)
                {
                    // Something still refers to it: it stays restorable and is tried again next hour.
                    logger.LogWarning(ex, "Trashed media file {MediaFileId} of site {SiteId} could not be purged; it stays restorable.", entry.Id, entry.SiteId);
                }
            }
        }

        if (purged > 0)
        {
            logger.LogInformation("Purged {Count} trashed media file(s) past the retention period.", purged);
        }

        return purged;
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
}
