using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.Items.RollbackItem;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.Trash;

internal sealed record ItemRestorePlanQuery(Guid SiteId, Guid ItemId) : IQuery<Result<RestorePlanResponse>>;

internal sealed record RestoreItemCommand(Guid SiteId, Guid ItemId, RestoreRequest Request) : ICommand<Result<RestoreResultResponse>>;

/// <summary>
/// Plans and performs an item's restore (021 FR-014, FR-015, FR-017, FR-018, research TR-10, TR-11).
/// </summary>
/// <remarks>
/// Written through <see cref="ItemWrites"/> like every item write, so a running schema change
/// refuses it (014 FR-037), the item is validated against the current schema and marked as
/// needing attention rather than refused (FR-018), and its slug and references come back
/// through <see cref="ItemIndexer"/>. "Restore and publish" runs 015's rollback to the last
/// live version in the same write (TR-11).
/// </remarks>
internal sealed class RestoreItemHandler(CmsTrash trash, ItemWrites writes, CmsAccess access, ILogger<RestoreItemHandler> logger)
    : IQueryHandler<ItemRestorePlanQuery, Result<RestorePlanResponse>>, ICommandHandler<RestoreItemCommand, Result<RestoreResultResponse>>
{
    public async Task<Result<RestorePlanResponse>> HandleAsync(ItemRestorePlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestorePlanResponse>(CmsErrors.SiteNotFound);
        }

        var item = await trash.TrashedItems(query.SiteId).AsNoTracking().FirstOrDefaultAsync(i => i.Id == query.ItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<RestorePlanResponse>(CmsErrors.NotInTrash);
        }

        using (item)
        {
            return Result.Success(await trash.PlanAsync(item, cancellationToken).ConfigureAwait(false));
        }
    }

    public async Task<Result<RestoreResultResponse>> HandleAsync(RestoreItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(CmsErrors.SiteNotFound);
        }

        if (request.Publish && !await trash.MayAsync(CmsCapabilities.ItemsPublish, command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(CmsErrors.Forbidden);
        }

        var collectionId = await trash.TrashedItems(command.SiteId)
            .Where(i => i.Id == command.ItemId)
            .Select(i => (Guid?)i.CollectionId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (collectionId is null)
        {
            return Result.Failure<RestoreResultResponse>(CmsErrors.NotInTrash);
        }

        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(command.SiteId, collectionId.Value, command.ItemId, null, null, access.Actor, FromTrash: true),
                async context =>
                {
                    var item = context.Item!;
                    var plan = await trash.PlanAsync(item, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(plan.Address, request.ConfirmedAddress, StringComparison.Ordinal))
                    {
                        return Result.Failure<Item?>(CmsErrors.PlanChanged(plan));
                    }

                    item.Restore(plan.Address, context.Actor, context.Now);
                    context.Changed(item);

                    if (request.Publish)
                    {
                        var number = await trash.LastLiveVersionAsync(command.SiteId, item.Id, cancellationToken).ConfigureAwait(false);
                        var version = number is { } n ? await RollbackItemHandler.VersionAsync(context, n, cancellationToken).ConfigureAwait(false) : null;
                        if (version is null)
                        {
                            return Result.Failure<Item?>(CmsErrors.VersionNotFound);
                        }

                        var published = await ItemPublishing.PublishAsync(
                                context, version.ReadValues(), version.Slug, ItemVersionKinds.Rollback, version.Number, cancelSchedule: true, null, cancellationToken)
                            .ConfigureAwait(false);
                        if (published.IsFailure)
                        {
                            return Result.Failure<Item?>(published.Error);
                        }
                    }

                    return Result.Success<Item?>(item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (written.IsFailure)
        {
            // ItemWrites answers "not found" when a restore or purge took the item first (FR-020).
            return Result.Failure<RestoreResultResponse>(written.Error.Code == CmsErrors.ItemNotFound.Code ? CmsErrors.NotInTrash : written.Error);
        }

        logger.LogInformation("Item {ItemId} restored from the trash of site {SiteId} (published: {Published}).", command.ItemId, command.SiteId, request.Publish);
        return Result.Success(new RestoreResultResponse([new RestoredResponse(TrashKinds.Item, command.ItemId, written.Value!.Slug)], request.Publish));
    }
}

/// <summary>Administrative surface: restore a trashed item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/trash/items/{itemId:guid}")]
public sealed class RestoreItemController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>What restoring the item would do (TR-10).</summary>
    [HttpGet("restore-plan")]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType<RestorePlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlanAsync(Guid siteId, Guid itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ItemRestorePlanQuery(siteId, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Restores the item, optionally publishing its last live version.</summary>
    [HttpPost("restore")]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType<RestoreResultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, Guid itemId, [FromBody] RestoreRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RestoreItemCommand(siteId, itemId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
