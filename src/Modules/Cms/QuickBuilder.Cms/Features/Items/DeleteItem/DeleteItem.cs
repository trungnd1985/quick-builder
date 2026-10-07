using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.DeleteItem;

internal sealed record DeleteItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId, int Revision) : ICommand<Result>;

/// <summary>
/// Deletes an item permanently with its versions, schedules and history (015 US5, FR-008).
/// Refused while other items refer to it, listing them (clarification Q3); the database's
/// foreign key catches a reference added at the same moment (research IT-10).
/// </summary>
internal sealed class DeleteItemHandler(ItemWrites writes, ItemReferrers referrers, CmsAccess access)
    : ICommandHandler<DeleteItemCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(CmsErrors.SiteNotFound);
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

                    context.Delete(context.Item!);
                    return Result.Success<Item?>(null);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (written.IsFailure && written.Error.Code == "cms.item_referenced")
        {
            // The race: something started referring to it after the check. Read who, to answer.
            return Result.Failure(await RefusalAsync(command, cancellationToken).ConfigureAwait(false) ?? written.Error);
        }

        return written.IsFailure ? Result.Failure(written.Error) : Result.Success();
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
    [HttpDelete]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid collectionId, Guid itemId, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteItemCommand(siteId, collectionId, itemId, revision), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
