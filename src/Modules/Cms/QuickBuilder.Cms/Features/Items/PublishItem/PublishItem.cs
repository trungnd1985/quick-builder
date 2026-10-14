using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.PublishItem;

internal sealed record PublishItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId, PublishingRequest Request) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Publishes an item's draft as a new version (015 US2, FR-019, FR-020, FR-027). Refused while
/// the draft needs attention; allowed with references to unpublished items, which the response
/// lists so the member can be told (clarification Q4).
/// </summary>
internal sealed class PublishItemHandler(ItemWrites writes, ItemReads reads, CmsDbContext database, CmsAccess access)
    : ICommandHandler<PublishItemCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(PublishItemCommand command, CancellationToken cancellationToken)
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
                    var item = context.Item!;
                    var published = await ItemPublishing.PublishAsync(
                            context, item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, command.Request.CancelSchedule, null, command.Request.RedirectOldAddress, cancellationToken)
                        .ConfigureAwait(false);
                    redirectRequested = context.RedirectRequested;
                    return published.IsFailure ? Result.Failure<Item?>(published.Error) : Result.Success<Item?>(item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (written.IsFailure)
        {
            return Result.Failure<ItemResponse>(written.Error);
        }

        var unpublished = await UnpublishedReferencesAsync(database, command.SiteId, command.ItemId, cancellationToken).ConfigureAwait(false);
        var answered = await ItemSlices.AnswerAsync(written, reads, unpublished, cancellationToken).ConfigureAwait(false);
        return answered.IsFailure ? answered : Result.Success(answered.Value with { RedirectRequested = redirectRequested });
    }

    /// <summary>The items the published values refer to that have nothing published (FR-017, FR-020).</summary>
    internal static async Task<IReadOnlyList<ItemRefResponse>> UnpublishedReferencesAsync(
        CmsDbContext database, Guid siteId, Guid itemId, CancellationToken cancellationToken)
    {
        var targets = database.ForSite<ItemReference>(siteId)
            .Where(r => r.SourceItemId == itemId && r.InPublished)
            .Select(r => r.TargetItemId);

        return (await database.ForSite<Item>(siteId)
                .AsNoTracking()
                .Where(i => targets.Contains(i.Id) && i.Status == ItemStatuses.Draft)
                .OrderBy(i => i.Name)
                .ThenBy(i => i.Slug)
                .Select(i => new { i.Id, i.CollectionId, i.Name, i.Slug, i.Status })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(i => new ItemRefResponse(i.Id, i.CollectionId, i.Name, i.Slug, i.Status))
            .ToList();
    }
}

/// <summary>Administrative surface: publish an item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/publish")]
public sealed class PublishItemController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(CmsCapabilities.ItemsPublish)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishAsync(Guid siteId, Guid collectionId, Guid itemId, PublishingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new PublishItemCommand(siteId, collectionId, itemId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
