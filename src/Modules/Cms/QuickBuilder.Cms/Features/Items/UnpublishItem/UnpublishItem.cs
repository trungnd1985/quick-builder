using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.UnpublishItem;

internal sealed record UnpublishItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId, PublishingRequest Request) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Removes an item's published values, keeping its draft and versions (015 US2, FR-021, FR-027).
/// The admin shows how many published items refer to it before confirming, from the referrers.
/// </summary>
internal sealed class UnpublishItemHandler(ItemWrites writes, ItemReads reads, CmsAccess access)
    : ICommandHandler<UnpublishItemCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(UnpublishItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        var redirectRequested = false;
        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, command.Request.Revision, null, access.Actor),
                async context =>
                {
                    var unpublished = await ItemPublishing.UnpublishAsync(context, command.Request.CancelSchedule, null, command.Request.RedirectToPageId, cancellationToken).ConfigureAwait(false);
                    redirectRequested = context.RedirectRequested;
                    return unpublished.IsFailure ? Result.Failure<Item?>(unpublished.Error) : Result.Success<Item?>(context.Item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        var answered = await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
        return answered.IsFailure ? answered : Result.Success(answered.Value with { RedirectRequested = redirectRequested });
    }
}

/// <summary>Administrative surface: unpublish an item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/unpublish")]
public sealed class UnpublishItemController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(CmsCapabilities.ItemsPublish)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnpublishAsync(Guid siteId, Guid collectionId, Guid itemId, PublishingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new UnpublishItemCommand(siteId, collectionId, itemId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
