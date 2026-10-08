using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.SaveItemDraft;

internal sealed record SaveItemDraftCommand(Guid SiteId, Guid CollectionId, Guid ItemId, DraftRequest Request) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Saves an item's draft (015 US1, FR-005, FR-013, FR-014). Never touches its published values
/// (SC-004): only publishing does. Refused when the item or the schema changed since the form
/// was built, or when a value can never be stored.
/// </summary>
internal sealed class SaveItemDraftHandler(ItemWrites writes, ItemReads reads, CmsAccess access)
    : ICommandHandler<SaveItemDraftCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(SaveItemDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        var draft = ItemSlices.ReadDraft(command.Request);
        if (draft.IsFailure)
        {
            return Result.Failure<ItemResponse>(draft.Error);
        }

        var (slug, values) = draft.Value;
        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(
                    command.SiteId, command.CollectionId, command.ItemId, command.Request.Revision, command.Request.SchemaRevision, access.Actor),
                async context =>
                {
                    var item = context.Item!;
                    var report = await context.ValidateAsync(item.Id, slug, values, cancellationToken).ConfigureAwait(false);
                    if (ItemSlices.RefuseMalformed(report) is { } refused)
                    {
                        return Result.Failure<Item?>(refused);
                    }

                    item.SaveDraft(slug, values, context.Actor, context.Now);
                    context.Changed(item);
                    return Result.Success<Item?>(item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: save an item's draft.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}")]
public sealed class SaveItemDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ItemSizeLimit]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> SaveAsync(Guid siteId, Guid collectionId, Guid itemId, DraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new SaveItemDraftCommand(siteId, collectionId, itemId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
