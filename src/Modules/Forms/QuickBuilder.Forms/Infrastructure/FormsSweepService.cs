using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Forms.Domain;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// Removes what Forms keeps only for a while (018 FR-016a, FR-019, FR-021, FR-023a; research
/// FS-17), hourly, so "at least once a day" holds with margin: submissions past their form's
/// retention period, spam after 30 days, rate windows after an hour and spam counters after
/// 90 days.
/// </summary>
/// <remarks>
/// Modelled on Identity's retention service: each form is emptied in batches of
/// <see cref="BatchSize"/>, so a first sweep over years of submissions never holds one long
/// lock, and each run that deletes records the form and the count, never a value. A form uses
/// its last published setting, also after it is no longer published.
/// </remarks>
internal sealed class FormsSweepService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<FormsSweepService> logger) : BackgroundService
{
    internal const int BatchSize = 10_000;

    /// <summary>How long a submission caught by the trap stays in Spam (clarification 5).</summary>
    internal static readonly TimeSpan SpamRetention = TimeSpan.FromDays(30);

    /// <summary>How long a rate window is kept: the longest window, an hour (FR-021).</summary>
    internal static readonly TimeSpan RateWindowRetention = TimeSpan.FromHours(1);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Forms sweep failed; retrying next interval.");
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

    /// <summary>One sweep. Returns how many records it removed. Exposed for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<SubmissionLog>();
        var now = clock.GetUtcNow();
        var total = 0;

        foreach (var (siteId, formId, days) in await database.FormsWithRetentionAsync(cancellationToken).ConfigureAwait(false))
        {
            var before = now.AddDays(-days);
            var removed = await DeleteInBatchesAsync(
                    database.ForSite<Submission>(siteId).Where(s => s.FormId == formId && !s.IsSpam && s.ReceivedAtUtc < before),
                    cancellationToken)
                .ConfigureAwait(false);

            if (removed > 0)
            {
                log.Add(siteId, actor: null, SubmissionLogActions.RetentionPurge, formId, removed);
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Deleted {Count} submission(s) of form {FormId} on site {SiteId} past their retention period.", removed, formId, siteId);
            }

            total += removed;
        }

        total += await PurgeSpamAsync(database, log, now, cancellationToken).ConfigureAwait(false);
        total += await DeleteAllAsync(batch => database.DeleteRateWindowsAsync(now - RateWindowRetention, batch, cancellationToken)).ConfigureAwait(false);
        total += await DeleteAllAsync(batch => database.DeleteSpamCountersAsync(
            DateOnly.FromDateTime((now - SpamCounter.Retention).UtcDateTime), batch, cancellationToken)).ConfigureAwait(false);

        return total;
    }

    /// <summary>Spam older than 30 days, in batches, with one log row per site and form.</summary>
    private async Task<int> PurgeSpamAsync(FormsDbContext database, SubmissionLog log, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var removed = new Dictionary<(Guid SiteId, string FormId), int>();
        while (true)
        {
            var batch = await database.PurgeSpamAsync(now - SpamRetention, BatchSize, cancellationToken).ConfigureAwait(false);
            foreach (var row in batch)
            {
                removed[(row.SiteId, row.FormId)] = removed.GetValueOrDefault((row.SiteId, row.FormId)) + 1;
            }

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        foreach (var ((siteId, formId), count) in removed)
        {
            log.Add(siteId, actor: null, SubmissionLogActions.SpamPurge, formId, count);
            logger.LogInformation("Deleted {Count} spam submission(s) of form {FormId} on site {SiteId} after 30 days.", count, formId, siteId);
        }

        if (removed.Count > 0)
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return removed.Values.Sum();
    }

    /// <summary>Runs a batched delete until a batch comes back short.</summary>
    private static async Task<int> DeleteAllAsync(Func<int, Task<int>> deleteBatch)
    {
        var total = 0;
        while (true)
        {
            var deleted = await deleteBatch(BatchSize).ConfigureAwait(false);
            total += deleted;
            if (deleted < BatchSize)
            {
                return total;
            }
        }
    }

    /// <summary>Deletes what <paramref name="query"/> selects, at most <see cref="BatchSize"/> rows per statement.</summary>
    private static async Task<int> DeleteInBatchesAsync(IQueryable<Submission> query, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var ids = await query.Select(s => s.Id).Take(BatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return total;
            }

            total += await query.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Count < BatchSize)
            {
                return total;
            }
        }
    }
}
