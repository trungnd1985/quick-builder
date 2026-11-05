using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer;

internal sealed record GetTransferQuery(Guid TransferId) : IQuery<Result<TransferResponse>>;

/// <summary>One transfer: its state, progress, check result, summary or report (020 FR-007).</summary>
internal sealed class GetTransferHandler(SitesDbContext database, IUserDirectory users, TimeProvider clock)
    : IQueryHandler<GetTransferQuery, Result<TransferResponse>>
{
    public async Task<Result<TransferResponse>> HandleAsync(GetTransferQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var job = await database.TransferJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == query.TransferId, cancellationToken)
            .ConfigureAwait(false);

        return job is null
            ? Result.Failure<TransferResponse>(TransferErrors.NotFound)
            : Result.Success(await TransferResponses.BuildAsync(job, users, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false));
    }
}

internal sealed record ListTransfersQuery(Guid SiteId) : IQuery<IReadOnlyList<TransferResponse>>;

/// <summary>A site's exports and imports of the last 90 days, newest first (admin-transfer.md §1).</summary>
internal sealed class ListTransfersHandler(SitesDbContext database, IUserDirectory users, TimeProvider clock)
    : IQueryHandler<ListTransfersQuery, IReadOnlyList<TransferResponse>>
{
    private const int MaxListed = 100;

    public async Task<IReadOnlyList<TransferResponse>> HandleAsync(ListTransfersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Installation data ([InstallationScoped]), filtered here by the site named in the path.
        var jobs = await database.TransferJobs.AsNoTracking()
            .Where(j => j.SiteId == query.SiteId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(MaxListed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await TransferResponses.BuildManyAsync(jobs, users, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record CancelTransferCommand(Guid TransferId) : ICommand<Result<TransferResponse>>;

/// <summary>
/// Asks a transfer to stop (020 FR-007). One no worker holds stops at once; a running one stops
/// at its next batch, and an import rolls back (FR-018).
/// </summary>
internal sealed class CancelTransferHandler(TransferJobStore store, IUserDirectory users, TimeProvider clock)
    : ICommandHandler<CancelTransferCommand, Result<TransferResponse>>
{
    public async Task<Result<TransferResponse>> HandleAsync(CancelTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await store.FindAsync(command.TransferId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return Result.Failure<TransferResponse>(TransferErrors.NotFound);
        }

        var now = clock.GetUtcNow();
        var cancelled = job.RequestCancel(now);
        if (cancelled.IsFailure)
        {
            return Result.Failure<TransferResponse>(cancelled.Error!);
        }

        var saved = await store
            .SaveAsync(job, job.IsFinal ? [TransferOutcomes.FinishedEvent(job, now)] : null, cancellationToken)
            .ConfigureAwait(false);
        if (saved != SaveOutcome.Saved)
        {
            return Result.Failure<TransferResponse>(TransferErrors.Changed);
        }

        return Result.Success(await TransferResponses.BuildAsync(job, users, now, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: transfers by id (020 contracts/transfer-api.yaml).</summary>
[ApiController]
[Route("api/v1/transfers")]
public sealed class TransfersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>State, progress, check result, summary or report of one transfer.</summary>
    [HttpGet("{transferId:guid}")]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetTransferQuery(transferId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Asks a transfer to stop.</summary>
    [HttpPost("{transferId:guid}/cancel")]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new CancelTransferCommand(transferId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : Accepted(result.Value);
    }
}

/// <summary>Administrative surface: a site's transfers (020 contracts/transfer-api.yaml).</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/transfers")]
public sealed class SiteTransfersController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The site's exports and imports of the last 90 days, newest first.</summary>
    [HttpGet]
    [RequireCapability(SitesCapabilities.TransferExport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(Guid siteId, CancellationToken cancellationToken) =>
        Ok(await dispatcher.QueryAsync(new ListTransfersQuery(siteId), cancellationToken).ConfigureAwait(false));
}
