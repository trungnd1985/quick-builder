using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.Items.Trash;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.DeleteItem;

internal sealed record DeleteItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId, int Revision, Guid? RedirectToPageId = null) : ICommand<Result<TrashReceiptResponse>>;

/// <summary>
/// Moves an item to the site's trash (015 US5, FR-008; 021 FR-001, FR-003). Refused while other
/// items refer to it, listing them (clarification Q3).
/// </summary>
/// <remarks>
/// A published item is unpublished as part of the delete and its schedules are cancelled
/// (TR-07). It keeps its draft, versions and history; it frees its slug and stops referring to
/// other items, so it blocks nothing (TR-04, TR-06). The existing <c>ItemDeleted</c> event
/// tells the rest of the product it is gone (TR-08).
/// </remarks>
internal sealed class DeleteItemHandler(ItemWrites writes, ItemReferrers referrers, CmsAccess access, TrashRetention retention, IEditingLocks locks)
    : ICommandHandler<DeleteItemCommand, Result<TrashReceiptResponse>>
{
    public async Task<Result<TrashReceiptResponse>> HandleAsync(DeleteItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<TrashReceiptResponse>(CmsErrors.SiteNotFound);
        }

        // 021 FR-008, TR-09: not while another member is editing it.
        if (await locks.FindActiveHolderAsync(command.SiteId, EditingDocumentKinds.Item, command.ItemId, access.Actor, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            var names = await access.NamesAsync([holder.UserId], cancellationToken).ConfigureAwait(false);
            return Result.Failure<TrashReceiptResponse>(CmsErrors.DeleteLockHeld(holder.UserId, names.GetValueOrDefault(holder.UserId) ?? string.Empty, holder.Since));
        }

        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, command.Revision, null, access.Actor),
                async context =>
                {
                    var refused = await RefusalAsync(command, cancellationToken).ConfigureAwait(false);
                    if (refused is not null)
                    {
                        return Result.Failure<Item?>(refused);
                    }

                    var trashed = await ItemPublishing.TrashAsync(context, command.RedirectToPageId, cancellationToken).ConfigureAwait(false);
                    return trashed.IsFailure ? Result.Failure<Item?>(trashed.Error) : Result.Success<Item?>(context.Item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (written.IsFailure)
        {
            return Result.Failure<TrashReceiptResponse>(written.Error);
        }

        var trashedAt = written.Value!.TrashedAtUtc!.Value;
        return Result.Success(new TrashReceiptResponse(trashedAt, retention.PurgeOn(trashedAt)));
    }

    private async Task<Shared.Kernel.Results.Error?> RefusalAsync(DeleteItemCommand command, CancellationToken cancellationToken)
    {
        var list = await referrers.ListAsync(command.SiteId, command.ItemId, publishedOnly: false, cancellationToken).ConfigureAwait(false);
        return list.Total == 0 ? null : CmsErrors.ItemReferenced([.. list.Referrers], list.Total);
    }
}

/// <summary>Administrative surface: delete an item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}")]
public sealed class DeleteItemController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Moves the item to the trash, where it can be restored until the receipt's date (021 FR-002).</summary>
    [HttpDelete]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType<TrashReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid siteId, Guid collectionId, Guid itemId, [FromQuery] int revision, [FromQuery] Guid? redirectToPageId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteItemCommand(siteId, collectionId, itemId, revision, redirectToPageId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
