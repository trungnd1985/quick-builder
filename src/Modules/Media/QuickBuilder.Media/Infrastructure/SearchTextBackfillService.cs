using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Media.Domain;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Text;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Gives files uploaded before 013 their accent-folded search text (013 research MV-26).
/// </summary>
/// <remarks>
/// <para>
/// The <c>AddLibraryAndVariants</c> migration can only emit SQL, so it filled
/// <c>search_text</c> with <c>lower(file_name)</c> and marked the rows provisional. This
/// rewrites them with <see cref="SearchText.Combine"/> after start-up. Until it has run, search
/// on an upgraded site ignores case but not accents; the upgrade notes say so.
/// </para>
/// <para>
/// <b>One site at a time</b> (Principle V: background jobs are namespaced by site). The only
/// cross-site read is the list of sites with work left. Each batch is then filtered to its
/// site explicitly. Idempotent: two instances compute the same value, and the row version
/// turns the slower one's save into a no-op.
/// </para>
/// </remarks>
internal sealed class SearchTextBackfillService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<SearchTextBackfillService> logger) : BackgroundService
{
    internal const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // After readiness, never in front of it.
        if (!await ApplicationStartedAsync(stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Media search-text backfill failed; it runs again at the next start.");
        }
    }

    /// <summary>Normalises every provisional row, site by site. Returns how many rows changed. Exposed for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        List<Guid> sites;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            sites = await database.MediaFiles
                .AcrossAllSites("Finds which sites still have provisional search text; each site is then processed on its own (013 MV-26).")
                .AsNoTracking()
                .Where(f => f.SearchTextState == SearchTextState.Provisional)
                .Select(f => f.SiteId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var total = 0;
        foreach (var siteId in sites)
        {
            var changed = await RunForSiteAsync(siteId, cancellationToken).ConfigureAwait(false);
            if (changed > 0)
            {
                logger.LogInformation("Media search-text backfill normalised {Count} file(s) on site {SiteId}.", changed, siteId);
            }

            total += changed;
        }

        return total;
    }

    private async Task<int> RunForSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var changed = 0;

        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

            var batch = await database.MediaFiles
                .AcrossAllSites("Background backfill of one site, named explicitly below (013 MV-26).")
                .Where(f => f.SiteId == siteId && f.SearchTextState == SearchTextState.Provisional)
                .OrderBy(f => f.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                return changed;
            }

            foreach (var file in batch)
            {
                file.RefreshSearchText();
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                changed += batch.Count;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance, or an edit, got there first. Its rows are already
                // normalised; anything still provisional is picked up by the next batch.
            }
        }
    }

    private async Task<bool> ApplicationStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));

        try
        {
            await started.Task.ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
