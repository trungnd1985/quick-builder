using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// Keeps a claimed job's lease alive while a runner works on it, and carries its progress
/// (020 research ST-15).
/// </summary>
/// <remarks>
/// Runs on its own scope and connection, never on the transfer's session: a lease renewal must
/// commit while the import's transaction is still open. Every second it writes the latest
/// progress if it changed; every 15 seconds it renews the lease. A cancel request, or a lease
/// that another instance has taken or that the job no longer has, cancels
/// <see cref="Token"/>, and the runner stops at its next batch.
/// </remarks>
internal sealed class TransferLease : ITransferProgress, IAsyncDisposable
{
    internal static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancel;
    private readonly Task _loop;
    private readonly object _gate = new();
    private string? _step;
    private double _fraction;
    private bool _dirty;
    private int _disposed;

    private TransferLease(Guid jobId, string owner, IServiceScopeFactory scopes, TimeProvider clock, ILogger logger, CancellationToken stopping)
    {
        JobId = jobId;
        Owner = owner;
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _loop = Task.Run(() => RunAsync(_cancel.Token), CancellationToken.None);
    }

    public Guid JobId { get; }

    public string Owner { get; }

    /// <summary>Cancelled when the job is asked to stop, the lease is lost, or the host stops.</summary>
    public CancellationToken Token => _cancel.Token;

    /// <summary>True when <see cref="Token"/> fired because an Administrator or the command asked to cancel.</summary>
    public bool CancelRequested { get; private set; }

    public static TransferLease Hold(Guid jobId, string owner, IServiceScopeFactory scopes, TimeProvider clock, ILogger logger, CancellationToken stopping) =>
        new(jobId, owner, scopes, clock, logger, stopping);

    /// <summary>Names the step whose progress <see cref="Report"/> reports next.</summary>
    public void Step(string step)
    {
        lock (_gate)
        {
            _step = step;
            _fraction = 0;
            _dirty = true;
        }
    }

    public void Report(double fraction)
    {
        lock (_gate)
        {
            _fraction = fraction;
            _dirty = true;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var lastRenewal = _clock.GetUtcNow();
        using var timer = new PeriodicTimer(Tick, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = _clock.GetUtcNow();
                var renew = now - lastRenewal >= RenewEvery;
                string? step;
                double fraction;
                bool dirty;
                lock (_gate)
                {
                    (step, fraction, dirty) = (_step, _fraction, _dirty);
                    _dirty = false;
                }

                if (!renew && !dirty)
                {
                    continue;
                }

                if (!await TouchAsync(step, fraction, dirty, renew, now, cancellationToken).ConfigureAwait(false))
                {
                    await _cancel.CancelAsync().ConfigureAwait(false);
                    return;
                }

                if (renew)
                {
                    lastRenewal = now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the runner, the host, or a cancel request.
        }
    }

    private async Task<bool> TouchAsync(string? step, double fraction, bool progress, bool renew, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<TransferJobStore>();
            var job = await store.FindAsync(JobId, cancellationToken).ConfigureAwait(false);
            if (job is null || job.LeaseOwner != Owner)
            {
                _logger.LogWarning("Transfer {TransferId} lost its lease; stopping.", JobId);
                return false;
            }

            if (job.CancelRequested)
            {
                CancelRequested = true;
                return false;
            }

            if (progress && step is not null)
            {
                job.ReportProgress(step, fraction, now);
            }

            if (renew && !job.RenewLease(Owner, now))
            {
                return false;
            }

            return await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false) == SaveOutcome.Saved;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missed renewal is retried next tick; the lease lasts 2 minutes.
            _logger.LogWarning(ex, "Could not renew the lease of transfer {TransferId}; retrying.", JobId);
            return true;
        }
    }

    /// <summary>Stops the lease. Idempotent: a runner stops it before saving the outcome, and the worker again after.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _cancel.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _cancel.Dispose();
    }
}
