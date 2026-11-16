using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.Trash;

/// <summary>Several trashed items to delete permanently, at most 100 (FR-021).</summary>
public sealed record PurgeManyRequest(IReadOnlyList<Guid> Ids);

/// <summary>Empty the item trash up to the moment the count was shown (FR-021, TR-14).</summary>
public sealed record EmptyTrashRequest(DateTimeOffset TrashedUpTo, int ExpectedItems);

/// <summary>What emptying removed.</summary>
public sealed record EmptyTrashResponse(int Items, IReadOnlyList<BulkFailureResponse> Failed);

internal sealed record PurgeItemCommand(Guid SiteId, Guid ItemId) : ICommand<Result>;

internal sealed record PurgeItemsCommand(Guid SiteId, IReadOnlyList<Guid> Ids) : ICommand<Result<BulkOutcomeResponse>>;

internal sealed record EmptyItemTrashCommand(Guid SiteId, EmptyTrashRequest Request) : ICommand<Result<EmptyTrashResponse>>;

/// <summary>Permanent delete of trashed items (021 US5, FR-021, FR-022). Administrators only.</summary>
internal sealed class PurgeHandler(CmsTrash trash, CmsTrashPurge purge, CmsAccess access)
    : ICommandHandler<PurgeItemCommand, Result>,
      ICommandHandler<PurgeItemsCommand, Result<BulkOutcomeResponse>>,
      ICommandHandler<EmptyItemTrashCommand, Result<EmptyTrashResponse>>
{
    private const int MaxEntries = 100;

    public async Task<Result> HandleAsync(PurgeItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return !await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false)
            ? Result.Failure(CmsErrors.SiteNotFound)
            : await purge.PurgeAsync(command.SiteId, command.ItemId, access.Actor, dueBefore: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BulkOutcomeResponse>> HandleAsync(PurgeItemsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<BulkOutcomeResponse>(CmsErrors.SiteNotFound);
        }

        if (command.Ids.Count is 0 or > MaxEntries)
        {
            return Result.Failure<BulkOutcomeResponse>(CmsErrors.InvalidQuery("ids"));
        }

        var succeeded = new List<Guid>();
        var failed = new List<BulkFailureResponse>();
        foreach (var id in command.Ids.Distinct())
        {
            var result = await purge.PurgeAsync(command.SiteId, id, access.Actor, dueBefore: null, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                succeeded.Add(id);
            }
            else
            {
                failed.Add(new BulkFailureResponse(id, result.Error.Code, result.Error.Message));
            }
        }

        return Result.Success(new BulkOutcomeResponse(succeeded, failed));
    }

    public async Task<Result<EmptyTrashResponse>> HandleAsync(EmptyItemTrashCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<EmptyTrashResponse>(CmsErrors.SiteNotFound);
        }

        var ids = await trash.TrashedItems(command.SiteId)
            .Where(i => i.TrashedAtUtc <= request.TrashedUpTo)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Count != request.ExpectedItems)
        {
            return Result.Failure<EmptyTrashResponse>(CmsErrors.CountChanged(ids.Count));
        }

        var failed = new List<BulkFailureResponse>();
        var purged = 0;
        foreach (var id in ids)
        {
            var result = await purge.PurgeAsync(command.SiteId, id, access.Actor, request.TrashedUpTo, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                purged++;
            }
            else
            {
                failed.Add(new BulkFailureResponse(id, result.Error.Code, result.Error.Message));
            }
        }

        return Result.Success(new EmptyTrashResponse(purged, failed));
    }
}

/// <summary>Administrative surface: delete trashed items permanently.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/trash")]
public sealed class PurgeController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Deletes one trashed item permanently.</summary>
    [HttpDelete("items/{itemId:guid}")]
    [RequireCapability(CmsCapabilities.TrashPurge)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PurgeAsync(Guid siteId, Guid itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new PurgeItemCommand(siteId, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : NoContent();
    }

    /// <summary>Deletes several trashed items permanently; each succeeds or fails on its own.</summary>
    [HttpPost("purge")]
    [RequireCapability(CmsCapabilities.TrashPurge)]
    [ProducesResponseType<BulkOutcomeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PurgeManyAsync(Guid siteId, [FromBody] PurgeManyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new PurgeItemsCommand(siteId, request.Ids ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Empties the item trash up to the moment the count was shown.</summary>
    [HttpPost("empty")]
    [RequireCapability(CmsCapabilities.TrashPurge)]
    [ProducesResponseType<EmptyTrashResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmptyAsync(Guid siteId, [FromBody] EmptyTrashRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new EmptyItemTrashCommand(siteId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
