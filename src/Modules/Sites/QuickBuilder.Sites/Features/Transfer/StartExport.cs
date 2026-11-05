using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer;

internal sealed record StartExportCommand(Guid SiteId, string Trigger) : ICommand<Result<TransferResponse>>;

/// <summary>
/// Queues an export of a site for the background worker (020 FR-006, FR-010).
/// </summary>
/// <remarks>
/// At most one export or import holds a site at a time; the unique index on active transfers
/// decides, so two Administrators clicking at once get one export and one <c>transfer.busy</c>.
/// </remarks>
internal sealed class StartExportHandler(
    TransferJobStore store,
    ISiteDirectory sites,
    IAccessEvaluator access,
    IUserDirectory users,
    TimeProvider clock)
    : ICommandHandler<StartExportCommand, Result<TransferResponse>>
{
    public async Task<Result<TransferResponse>> HandleAsync(StartExportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<TransferResponse>(Error.NotFound("sites.not_found", "No such site."));
        }

        var now = clock.GetUtcNow();
        var job = TransferJob.StartExport(command.SiteId, command.Trigger, access.UserId, now);
        return await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false) switch
        {
            SaveOutcome.Saved => Result.Success(await TransferResponses.BuildAsync(job, users, now, cancellationToken).ConfigureAwait(false)),
            SaveOutcome.SiteBusy => Result.Failure<TransferResponse>(TransferErrors.Busy),
            _ => Result.Failure<TransferResponse>(TransferErrors.Changed),
        };
    }
}

/// <summary>Administrative surface: exporting a site (020 contracts/transfer-api.yaml).</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/exports")]
public sealed class StartExportController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Queues an export of the site; follow it with <c>GET /api/v1/transfers/{id}</c>.</summary>
    [HttpPost]
    [RequireCapability(SitesCapabilities.TransferExport)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new StartExportCommand(siteId, TransferTriggers.Admin), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : Accepted(result.Value);
    }
}
