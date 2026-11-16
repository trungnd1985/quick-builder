using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.Trash;

/// <summary>One item to restore, and the slug the member saw.</summary>
public sealed record BulkRestoreEntryRequest(Guid Id, string? ConfirmedAddress);

/// <summary>Several items to restore, at most 100 (FR-019).</summary>
public sealed record BulkRestoreRequest(IReadOnlyList<BulkRestoreEntryRequest> Entries);

internal sealed record BulkRestoreCommand(Guid SiteId, IReadOnlyList<BulkRestoreEntryRequest> Entries) : ICommand<Result<BulkOutcomeResponse>>;

/// <summary>Restores several items, each on its own, and says what happened to each (021 FR-019).</summary>
internal sealed class BulkRestoreHandler(CmsAccess access, CmsDbContext database, RestoreItemHandler items)
    : ICommandHandler<BulkRestoreCommand, Result<BulkOutcomeResponse>>
{
    public async Task<Result<BulkOutcomeResponse>> HandleAsync(BulkRestoreCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<BulkOutcomeResponse>(CmsErrors.SiteNotFound);
        }

        if (command.Entries.Count is 0 or > 100)
        {
            return Result.Failure<BulkOutcomeResponse>(CmsErrors.InvalidQuery("entries"));
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkFailureResponse>();
        foreach (var entry in command.Entries)
        {
            var result = await items.HandleAsync(new RestoreItemCommand(command.SiteId, entry.Id, new RestoreRequest(null, entry.ConfirmedAddress)), cancellationToken).ConfigureAwait(false);
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

/// <summary>Administrative surface: restore several trashed items.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/trash/restore")]
public sealed class BulkRestoreController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Restores each item or says why it could not be.</summary>
    [HttpPost]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType<BulkOutcomeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, [FromBody] BulkRestoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new BulkRestoreCommand(siteId, request.Entries ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
