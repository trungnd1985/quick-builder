using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.CreateItem;

internal sealed record CreateItemCommand(Guid SiteId, Guid CollectionId, DraftRequest Request) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Creates a draft item (015 US1, FR-013, FR-014): refused when a value can never be stored,
/// stored with its report otherwise, however incomplete (clarification Q2).
/// </summary>
internal sealed class CreateItemHandler(ItemWrites writes, ItemReads reads, CmsAccess access)
    : ICommandHandler<CreateItemCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(CreateItemCommand command, CancellationToken cancellationToken)
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
                new ItemWriteRequest(command.SiteId, command.CollectionId, null, null, command.Request.SchemaRevision, access.Actor),
                async context =>
                {
                    var report = await context.ValidateAsync(null, slug, values, cancellationToken).ConfigureAwait(false);
                    if (ItemSlices.RefuseMalformed(report) is { } refused)
                    {
                        return Result.Failure<Item?>(refused);
                    }

                    var item = Item.New(context.SiteId, context.CollectionId, slug, values, context.Actor, context.Now);
                    context.Add(item);
                    return Result.Success<Item?>(item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: create an item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items")]
public sealed class CreateItemController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ItemSizeLimit]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> CreateAsync(Guid siteId, Guid collectionId, DraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new CreateItemCommand(siteId, collectionId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? CmsErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"{Request.PathBase}/api/v1/cms/sites/{siteId}/collections/{collectionId}/items/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
