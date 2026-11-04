using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Features.Transfer.Engine;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// Runs site exports, checks and imports in the background, one at a time per instance
/// (020 research ST-15; FR-006, FR-017).
/// </summary>
/// <remarks>
/// Every 5 seconds it fails jobs whose worker vanished, then claims queued work until there is
/// none, as <c>NotificationWorker</c> does (018). Several instances share the queue safely:
/// claiming moves the job's <c>xmin</c>, so only one instance wins a job.
/// </remarks>
internal sealed class TransferWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TransferWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>This instance, as named in leases.</summary>
    internal static readonly string InstanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Running site transfers failed; retrying next interval.");
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

    /// <summary>Fails lapsed leases, then runs every claimable job. Returns how many it ran. Exposed for tests.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var interrupted = await scope.ServiceProvider.GetRequiredService<TransferJobStore>()
                .FailExpiredLeasesAsync(clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            if (interrupted > 0)
            {
                logger.LogWarning("{Count} site transfer(s) were interrupted and have failed.", interrupted);
            }
        }

        var ran = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var job = await scope.ServiceProvider.GetRequiredService<TransferJobStore>()
                .ClaimNextAsync(InstanceId, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            if (job is null)
            {
                return ran;
            }

            ran++;
            await RunAsync(job, cancellationToken).ConfigureAwait(false);
        }

        return ran;
    }

    /// <summary>Runs one claimed job in a scope of its own. Also used by the operator command.</summary>
    internal async Task RunAsync(TransferJob claimed, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var runner = services.GetServices<ITransferRunner>().FirstOrDefault(r => r.CanRun(claimed));

        await using var lease = TransferLease.Hold(claimed.Id, claimed.LeaseOwner ?? InstanceId, scopes, clock, logger, cancellationToken);
        try
        {
            if (runner is null)
            {
                throw new InvalidOperationException($"No runner for a {claimed.Kind} transfer in state '{claimed.State}'.");
            }

            logger.LogInformation("Running {Kind} transfer {TransferId}.", claimed.Kind, claimed.Id);
            await runner.RunAsync(claimed, lease, lease.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Transfer {TransferId} failed.", claimed.Id);
            await FailAsync(claimed.Id, ex is OperationCanceledException && lease.CancelRequested, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FailAsync(Guid jobId, bool cancelled, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
        var job = await store.FindAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.IsFinal)
        {
            return;
        }

        var now = clock.GetUtcNow();
        if (cancelled)
        {
            job.Cancelled(now);
        }
        else
        {
            job.Failed(TransferFailures.Unexpected, now);
        }

        await store.SaveAsync(job, [TransferOutcomes.FinishedEvent(job, now)], cancellationToken).ConfigureAwait(false);
    }
}
