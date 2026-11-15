using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Trash;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Deletes trashed pages and components permanently once they have been in the trash longer
/// than <c>Trash:RetentionDays</c> (021 US6, FR-024, research TR-13).
/// </summary>
/// <remarks>
/// Hourly, in batches of 100, every entry in its own transaction through <see cref="PagesTrashPurge"/>,
/// recorded as made by the system. Several instances may run it at once: the purge deletes a row
/// only while it is still in the trash and due, so each entry is purged once, and an entry whose
/// purge fails stays restorable and is tried again next hour.
/// </remarks>
internal sealed class PagesTrashPurgeService(
    IServiceScopeFactory scopes,
    TrashRetention retention,
    TimeProvider clock,
    ILogger<PagesTrashPurgeService> logger) : BackgroundService
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
                logger.LogError(ex, "The trash purge of pages and components failed; retrying next hour.");
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
            var database = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
            var purge = scope.ServiceProvider.GetRequiredService<PagesTrashPurge>();

            var pages = await database.ForBackgroundWork<Page>().OnlyTrash(Reason)
                .Where(p => p.TrashedAtUtc <= due && !tried.Contains(p.Id))
                .OrderBy(p => p.TrashedAtUtc)
                .Take(BatchSize)
                .Select(p => new { p.SiteId, p.Id })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var components = await database.ForBackgroundWork<Component>().OnlyTrash(Reason)
                .Where(c => c.TrashedAtUtc <= due && !tried.Contains(c.Id))
                .OrderBy(c => c.TrashedAtUtc)
                .Take(BatchSize)
                .Select(c => new { c.SiteId, c.Id })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var batch = pages.Select(p => (Kind: SubjectKinds.Page, p.SiteId, p.Id))
                .Concat(components.Select(c => (Kind: SubjectKinds.Component, c.SiteId, c.Id)))
                .ToList();
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var (kind, siteId, id) in batch)
            {
                tried.Add(id);
                try
                {
                    // Another instance may have purged it, or a member restored it: nothing to do.
                    if ((await purge.PurgeAsync(siteId, kind, id, actor: null, due, cancellationToken).ConfigureAwait(false)).IsSuccess)
                    {
                        purged++;
                    }
                }
                catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "Trashed {SubjectKind} {SubjectId} of site {SiteId} could not be purged; it stays restorable.", kind, id, siteId);
                }
            }
        }

        if (purged > 0)
        {
            logger.LogInformation("Purged {Count} trashed page(s) and component(s) past the retention period.", purged);
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
