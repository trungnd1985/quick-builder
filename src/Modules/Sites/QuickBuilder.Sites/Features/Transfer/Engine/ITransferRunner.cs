using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// Runs one claimed job to its end: an export, the check of an upload, or an import
/// (020 research ST-06, ST-15).
/// </summary>
/// <remarks>
/// Resolved from a scope created for the job, so a runner may set that scope's site before any
/// part runs (analysis U2). The runner owns the job's final state: it stops the lease, loads the
/// job afresh and saves the outcome with its audit event. The worker only fails a job whose
/// runner threw.
/// </remarks>
internal interface ITransferRunner
{
    /// <summary>Whether this runner runs <paramref name="job"/> in its current, claimed state.</summary>
    bool CanRun(TransferJob job);

    Task RunAsync(TransferJob job, TransferLease lease, CancellationToken cancellationToken);
}
