using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.Trash;

/// <summary>One file to restore, and the destination the member saw.</summary>
public sealed record BulkRestoreEntryRequest(Guid Id, string? ConfirmedAddress, string? Placement = null, Guid? FolderId = null);

/// <summary>Several files to restore, at most 100 (FR-019).</summary>
public sealed record BulkRestoreRequest(IReadOnlyList<BulkRestoreEntryRequest> Entries);

internal sealed record BulkRestoreCommand(Guid SiteId, IReadOnlyList<BulkRestoreEntryRequest> Entries) : ICommand<Result<BulkOutcomeResponse>>;

/// <summary>Restores several files, each on its own, and says what happened to each (021 FR-019).</summary>
internal sealed class BulkRestoreHandler(ISiteDirectory sites, MediaDbContext database, RestoreFileHandler files)
    : ICommandHandler<BulkRestoreCommand, Result<BulkOutcomeResponse>>
{
    public async Task<Result<BulkOutcomeResponse>> HandleAsync(BulkRestoreCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<BulkOutcomeResponse>(MediaErrors.SiteNotFound);
        }

        if (command.Entries.Count is 0 or > 100)
        {
            return Result.Failure<BulkOutcomeResponse>(MediaErrors.TooManyFiles);
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkFailureResponse>();
        foreach (var entry in command.Entries)
        {
            var result = await files.HandleAsync(new RestoreFileCommand(command.SiteId, entry.Id, new RestoreRequest(entry.Placement, entry.FolderId, entry.ConfirmedAddress)), cancellationToken).ConfigureAwait(false);
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

/// <summary>Administrative surface: restore several trashed files.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/trash/restore")]
public sealed class BulkRestoreController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Restores each file or says why it could not be.</summary>
    [HttpPost]
    [RequireCapability(MediaCapabilities.DeleteOwn)]
    [ProducesResponseType<BulkOutcomeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, [FromBody] BulkRestoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new BulkRestoreCommand(siteId, request.Entries ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
