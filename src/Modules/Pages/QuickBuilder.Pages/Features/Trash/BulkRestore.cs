using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Trash;

/// <summary>One entry of a bulk restore: which, and the address the member saw.</summary>
public sealed record BulkRestoreEntryRequest(string Kind, Guid Id, string? ConfirmedAddress, string? Placement = null, Guid? ParentId = null);

/// <summary>Several entries to restore, at most 100 (FR-019).</summary>
public sealed record BulkRestoreRequest(IReadOnlyList<BulkRestoreEntryRequest> Entries);

internal sealed record BulkRestoreCommand(Guid SiteId, IReadOnlyList<BulkRestoreEntryRequest> Entries) : ICommand<Result<BulkOutcomeResponse>>;

/// <summary>
/// Restores several pages and components, each on its own, and says what happened to each
/// (021 FR-019). The endpoint needs only the view capability; each entry needs what its own
/// restore needs, so a member may restore the kinds they may delete.
/// </summary>
internal sealed class BulkRestoreHandler(PagesTrash trash, PagesAccess access, PagesDbContext database, RestorePageHandler pages, RestoreComponentHandler components)
    : ICommandHandler<BulkRestoreCommand, Result<BulkOutcomeResponse>>
{
    public async Task<Result<BulkOutcomeResponse>> HandleAsync(BulkRestoreCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<BulkOutcomeResponse>(PagesErrors.SiteNotFound);
        }

        if (command.Entries.Count is 0 or > PurgeHandler.MaxEntries || command.Entries.Any(e => e.Kind is not (TrashKinds.Page or TrashKinds.Component)))
        {
            return Result.Failure<BulkOutcomeResponse>(PagesErrors.InvalidPlacement);
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkFailureResponse>();
        foreach (var entry in command.Entries)
        {
            var request = new RestoreRequest(entry.Placement, entry.ParentId, entry.ConfirmedAddress);
            Result<RestoreResultResponse> result;
            if (!await trash.MayAsync(PagesTrash.RestoreCapability(entry.Kind), command.SiteId, cancellationToken).ConfigureAwait(false))
            {
                result = Result.Failure<RestoreResultResponse>(PagesErrors.Forbidden);
            }
            else if (entry.Kind == TrashKinds.Page)
            {
                result = await pages.HandleAsync(new RestorePageCommand(command.SiteId, entry.Id, request), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await components.HandleAsync(new RestoreComponentCommand(command.SiteId, entry.Id, request), cancellationToken).ConfigureAwait(false);
            }

            // Each entry stands alone: nothing one left behind may reach the next.
            database.ChangeTracker.Clear();
            if (result.IsSuccess)
            {
                succeeded.Add(entry.Id);
            }
            else
            {
                failed.Add(new BulkFailureResponse(entry.Id, result.Error.Code, result.Error.Message));
            }
        }

        return Result.Success(new BulkOutcomeResponse(succeeded, failed));
    }
}

/// <summary>Administrative surface: restore several trashed pages and components.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/trash/restore")]
public sealed class BulkRestoreController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Restores each entry or says why it could not be.</summary>
    [HttpPost]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType<BulkOutcomeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, [FromBody] BulkRestoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new BulkRestoreCommand(siteId, request.Entries ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
