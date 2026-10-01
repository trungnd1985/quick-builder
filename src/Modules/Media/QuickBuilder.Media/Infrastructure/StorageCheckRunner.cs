using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using QuickBuilder.Media.Domain;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// The storage consistency check (013 research MV-21, contracts/storage-providers.md §6):
/// which library files' current originals the configured provider does not have.
/// </summary>
/// <remarks>
/// <para>
/// <b>One at a time, across instances.</b> A run is claimed by inserting a <c>running</c> row,
/// which a partial unique index allows only once. A run renews its lease every 30 seconds;
/// one whose lease has lapsed belonged to a process that died, and the next claimer marks it
/// failed.
/// </para>
/// <para>
/// <b>Per site.</b> Each site's originals are listed and its rows compared separately
/// (Principle V). Variants are never reported: they are produced again on request.
/// </para>
/// </remarks>
internal sealed class StorageCheckRunner(
    IServiceScopeFactory scopes,
    IFileStorage storage,
    IOptions<StorageOptions> storageOptions,
    IHostApplicationLifetime lifetime,
    TimeProvider clock,
    ILogger<StorageCheckRunner> logger)
{
    internal static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan Renewal = TimeSpan.FromSeconds(30);

    /// <summary>The configured provider, spelled as the supported list spells it.</summary>
    public string Provider { get; } = StorageOptions.SupportedProviders.First(p =>
        string.Equals(p, storageOptions.Value.Provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>Claims a new run, or returns null when one is already running.</summary>
    public async Task<StorageCheck?> TryClaimAsync(string trigger, Guid? startedBy, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var now = clock.GetUtcNow();

        // A run whose lease lapsed was left by a process that stopped mid-run.
        await database.StorageChecks
            .Where(c => c.Status == StorageCheckStatus.Running && c.LeaseUntilUtc < now)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.Status, StorageCheckStatus.Failed).SetProperty(c => c.FinishedAtUtc, now),
                cancellationToken)
            .ConfigureAwait(false);

        var check = new StorageCheck(Guid.CreateVersion7(), trigger, Provider, now, now + Lease, startedBy);
        database.StorageChecks.Add(check);
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return check;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a claimed check in the background, until it finishes or the application stops.
    /// </summary>
    public void Start(Guid checkId) =>
        _ = Task.Run(() => RunAsync(checkId, lifetime.ApplicationStopping), CancellationToken.None);

    /// <summary>Runs a claimed check to its end and returns it as it finished. Never throws for a storage failure.</summary>
    public async Task<StorageCheck> RunAsync(Guid checkId, CancellationToken cancellationToken)
    {
        var filesChecked = 0;
        var missing = 0;
        var listed = 0;
        var status = StorageCheckStatus.Failed;

        try
        {
            foreach (var siteId in await SitesAsync(cancellationToken).ConfigureAwait(false))
            {
                var (siteChecked, siteMissing) = await CheckSiteAsync(checkId, siteId, listed, cancellationToken).ConfigureAwait(false);
                filesChecked += siteChecked;
                missing += siteMissing.Count;
                listed += siteMissing.Listed;

                if (siteMissing.Count > 0)
                {
                    logger.LogWarning(
                        "Storage check {CheckId}: {Missing} of {Checked} file(s) on site {SiteId} have no stored original.",
                        checkId,
                        siteMissing.Count,
                        siteChecked,
                        siteId);
                }

                await SaveProgressAsync(checkId, filesChecked, missing, cancellationToken).ConfigureAwait(false);
            }

            status = StorageCheckStatus.Completed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a provider message may echo request details.
            logger.LogError("Storage check {CheckId} failed ({ErrorType}).", checkId, ex.GetType().Name);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Storage check {CheckId} stopped with the application.", checkId);
        }

        var check = await FinishAsync(checkId, status, filesChecked, missing).ConfigureAwait(false);
        logger.LogInformation(
            "Storage check {CheckId} ({Trigger}) {Status}: {Checked} file(s) checked, {Missing} missing.",
            checkId,
            check.Trigger,
            check.Status,
            check.FilesChecked,
            check.MissingCount);
        return check;
    }

    private async Task<List<Guid>> SitesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediaDbContext>().MediaFiles
            .AcrossAllSites("The storage check describes the installation's storage; each site is then checked on its own (013 MV-21).")
            .Select(f => f.SiteId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(int Checked, (int Count, int Listed) Missing)> CheckSiteAsync(
        Guid checkId,
        Guid siteId,
        int alreadyListed,
        CancellationToken cancellationToken)
    {
        var stored = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var entry in storage.ListAsync(MediaStorageKeys.SitePrefix(siteId), cancellationToken).ConfigureAwait(false))
        {
            stored.Add(entry.Key.Value);
        }

        await using var readScope = scopes.CreateAsyncScope();
        await using var writeScope = scopes.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var writer = writeScope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var filesChecked = 0;
        var missing = 0;
        var listed = 0;
        var lastRenewal = clock.GetUtcNow();

        var rows = reader.MediaFiles
            .AcrossAllSites("Background storage check of one site, named explicitly below (013 MV-21).")
            .AsNoTracking()
            .Where(f => f.SiteId == siteId)
            .Select(f => new { f.Id, f.StorageKey })
            .AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            filesChecked++;
            if (!stored.Contains(row.StorageKey))
            {
                missing++;
                if (alreadyListed + listed < StorageCheck.MaxListedMissing)
                {
                    writer.StorageCheckMissing.Add(new StorageCheckMissing(checkId, siteId, row.Id));
                    listed++;
                }
            }

            if (clock.GetUtcNow() - lastRenewal >= Renewal)
            {
                await writer.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await RenewAsync(writer, checkId, cancellationToken).ConfigureAwait(false);
                lastRenewal = clock.GetUtcNow();
            }
        }

        await writer.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (filesChecked, (missing, listed));
    }

    private Task<int> RenewAsync(MediaDbContext database, Guid checkId, CancellationToken cancellationToken)
    {
        var lease = clock.GetUtcNow() + Lease;
        return database.StorageChecks
            .Where(c => c.Id == checkId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntilUtc, lease), cancellationToken);
    }

    private async Task SaveProgressAsync(Guid checkId, int filesChecked, int missing, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var lease = clock.GetUtcNow() + Lease;
        await scope.ServiceProvider.GetRequiredService<MediaDbContext>().StorageChecks
            .Where(c => c.Id == checkId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.FilesChecked, filesChecked)
                    .SetProperty(c => c.MissingCount, missing)
                    .SetProperty(c => c.LeaseUntilUtc, lease),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<StorageCheck> FinishAsync(Guid checkId, string status, int filesChecked, int missing)
    {
        // Not the caller's token: a run stopped by shutdown still records how it ended.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var check = await database.StorageChecks.SingleAsync(c => c.Id == checkId, timeout.Token).ConfigureAwait(false);

        var now = clock.GetUtcNow();
        check.Progress(filesChecked, missing, now);
        check.Finish(status, now);
        await database.SaveChangesAsync(timeout.Token).ConfigureAwait(false);
        return check;
    }
}

/// <summary>
/// Runs the consistency check once after start-up, in the background (013 clarification:
/// the start-up check never delays readiness).
/// </summary>
/// <remarks>
/// Several instances starting together run it once: a run that began after this process
/// started already answers the question, and the claim admits one running check at a time.
/// </remarks>
internal sealed class StorageConsistencyCheckService(
    StorageCheckRunner runner,
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<StorageConsistencyCheckService> logger) : BackgroundService
{
    private static readonly DateTimeOffset ProcessStarted = new(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("The start-up storage check could not start ({ErrorType}).", ex.GetType().Name);
        }
    }

    /// <summary>Starts and runs the start-up check unless one already ran since this process started. Exposed for tests.</summary>
    internal async Task<StorageCheck?> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var recent = await scope.ServiceProvider.GetRequiredService<MediaDbContext>().StorageChecks
                .AnyAsync(c => c.StartedAtUtc >= ProcessStarted, cancellationToken)
                .ConfigureAwait(false);
            if (recent)
            {
                return null;
            }
        }

        var check = await runner.TryClaimAsync(StorageCheckTrigger.Startup, startedBy: null, cancellationToken).ConfigureAwait(false);
        return check is null ? null : await runner.RunAsync(check.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> StartedAsync(CancellationToken stoppingToken)
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
