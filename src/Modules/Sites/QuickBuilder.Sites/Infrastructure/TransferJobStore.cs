using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// Reads and writes transfer jobs (020 data-model §1). Every write goes through the job's
/// <c>xmin</c>, so two instances never move one job at once.
/// </summary>
internal sealed class TransferJobStore(SitesDbContext database)
{
    private const string ActiveSiteIndex = "ux_transfer_jobs_active_site";

    public Task<TransferJob?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        database.TransferJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    /// <summary>The job holding the site's transfer slot (<see cref="TransferStates.HoldingSlotSql"/>), if any.</summary>
    public Task<TransferJob?> ActiveForSiteAsync(Guid siteId, CancellationToken cancellationToken) =>
        database.TransferJobs
            .AsNoTracking()
            .Where(j => j.SiteId == siteId && j.Trigger != TransferTriggers.PreRestore)
            .Where(j => (j.Kind == TransferKinds.Export && (j.State == TransferStates.Queued || j.State == TransferStates.Running))
                || (j.Kind == TransferKinds.Import && (j.State == TransferStates.Confirmed || j.State == TransferStates.Running)))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Saves a job, with the events of the change, in one transaction. False when the job
    /// changed since it was loaded, or when the change would give its site a second active
    /// transfer (FR-010).
    /// </summary>
    public async Task<SaveOutcome> SaveAsync(TransferJob job, IEnumerable<PendingEvent>? events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (database.Entry(job).State == EntityState.Detached)
        {
            database.TransferJobs.Add(job);
        }

        foreach (var integrationEvent in events ?? [])
        {
            SitesOutbox.Add(database, integrationEvent);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            return SaveOutcome.Changed;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ActiveSiteIndex })
        {
            database.ChangeTracker.Clear();
            return SaveOutcome.SiteBusy;
        }
    }

    /// <summary>Claims the oldest job a worker may run, or returns null (research ST-15).</summary>
    public async Task<TransferJob?> ClaimNextAsync(string owner, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await database.TransferJobs
            .AsNoTracking()
            .Where(j => (j.State == TransferStates.Queued || j.State == TransferStates.Confirmed) && !j.CancelRequested)
            .OrderBy(j => j.CreatedAtUtc)
            .Select(j => j.Id)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var id in candidates)
        {
            var job = await FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (job is null || !job.IsClaimable)
            {
                continue;
            }

            job.Claim(owner, nowUtc);
            if (await SaveAsync(job, null, cancellationToken).ConfigureAwait(false) == SaveOutcome.Saved)
            {
                return job;
            }
        }

        return null;
    }

    /// <summary>
    /// Fails jobs whose worker's lease ran out: the instance died, so its import rolled back with
    /// its connection (ST-05, ST-15). Returns how many.
    /// </summary>
    public async Task<int> FailExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var expired = await database.TransferJobs
            .Where(j => (j.State == TransferStates.Checking || j.State == TransferStates.Running) && j.LeaseUntilUtc < nowUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var failed = 0;
        foreach (var job in expired)
        {
            if (job.FailIfLeaseExpired(nowUtc)
                && await SaveAsync(job, [TransferOutcomes.FinishedEvent(job, nowUtc)], cancellationToken).ConfigureAwait(false) == SaveOutcome.Saved)
            {
                failed++;
            }
        }

        return failed;
    }
}

internal enum SaveOutcome
{
    Saved,

    /// <summary>The job changed since it was loaded.</summary>
    Changed,

    /// <summary>The site already has an active transfer (<c>transfer.busy</c>).</summary>
    SiteBusy,
}

/// <summary>The audit event that records how a transfer ended (research ST-19).</summary>
internal static class TransferOutcomes
{
    public static PendingEvent FinishedEvent(TransferJob job, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Kind == TransferKinds.Export
            ? PendingEvent.Of(new SiteExportFinished(Guid.CreateVersion7(), nowUtc, job.Id, job.SiteId!.Value, job.State, job.Trigger, job.StartedBy, job.FailureCode))
            : PendingEvent.Of(new SiteImportFinished(Guid.CreateVersion7(), nowUtc, job.Id, job.SiteId, job.Mode ?? string.Empty, job.State, job.Trigger, job.StartedBy, job.FailureCode));
    }
}
