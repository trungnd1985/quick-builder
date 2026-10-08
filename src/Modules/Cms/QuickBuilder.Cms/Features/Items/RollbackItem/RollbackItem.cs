using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.RollbackItem;

internal sealed record RollbackItemCommand(Guid SiteId, Guid CollectionId, Guid ItemId, int Number, PublishingRequest Request) : ICommand<Result<ItemResponse>>;

internal sealed record CopyVersionToDraftCommand(Guid SiteId, Guid CollectionId, Guid ItemId, int Number, int Revision) : ICommand<Result<ItemResponse>>;

/// <summary>
/// Publishes a kept version again as a new version, leaving the draft as it is (015 US7, FR-022):
/// refused with the reasons when its content fails today's rules, such as a slug now used by
/// another item or a reference to an item deleted since.
/// </summary>
internal sealed class RollbackItemHandler(ItemWrites writes, ItemReads reads, CmsAccess access)
    : ICommandHandler<RollbackItemCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(RollbackItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, command.Request.Revision, null, access.Actor),
                async context =>
                {
                    var version = await VersionAsync(context, command.Number, cancellationToken).ConfigureAwait(false);
                    if (version is null)
                    {
                        return Result.Failure<Item?>(CmsErrors.VersionNotFound);
                    }

                    var published = await ItemPublishing.PublishAsync(
                            context, version.ReadValues(), version.Slug, ItemVersionKinds.Rollback, version.Number, command.Request.CancelSchedule, null, cancellationToken)
                        .ConfigureAwait(false);
                    return published.IsFailure ? Result.Failure<Item?>(published.Error) : Result.Success<Item?>(context.Item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        // A version's slug now held by another item is reported like any problem of the version.
        if (written.IsFailure && written.Error.Code == "cms.item_slug_taken")
        {
            return Result.Failure<ItemResponse>(CmsErrors.VersionNotPublishable(
                ItemReads.Report(new ItemReport([new ItemProblem(ItemReport.SlugField, ItemProblems.InvalidSlug)]))));
        }

        return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
    }

    internal static Task<ItemVersion?> VersionAsync(ItemWriteContext context, int number, CancellationToken cancellationToken)
    {
        var itemId = context.Item!.Id;
        return context.Database.ForSite<ItemVersion>(context.SiteId)
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.ItemId == itemId && v.Number == number, cancellationToken);
    }
}

/// <summary>
/// Replaces the draft with a kept version's content; publishes nothing (015 US7, FR-022). Always
/// allowed: versions are migrated with the schema, so their values are well-formed, and anything
/// that no longer holds is reported on the draft like any other problem.
/// </summary>
internal sealed class CopyVersionToDraftHandler(ItemWrites writes, ItemReads reads, CmsAccess access)
    : ICommandHandler<CopyVersionToDraftCommand, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(CopyVersionToDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        var written = await writes.ExecuteAsync(
                new ItemWriteRequest(command.SiteId, command.CollectionId, command.ItemId, command.Revision, null, access.Actor),
                async context =>
                {
                    var version = await RollbackItemHandler.VersionAsync(context, command.Number, cancellationToken).ConfigureAwait(false);
                    if (version is null)
                    {
                        return Result.Failure<Item?>(CmsErrors.VersionNotFound);
                    }

                    context.Item!.CopyToDraft(version, context.Actor, context.Now);
                    context.Changed(context.Item);
                    return Result.Success<Item?>(context.Item);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await ItemSlices.AnswerAsync(written, reads, null, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: roll back to a version, or copy it into the draft.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/versions/{number:int}")]
public sealed class ItemVersionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost("rollback")]
    [RequireCapability(CmsCapabilities.ItemsPublish)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RollbackAsync(Guid siteId, Guid collectionId, Guid itemId, int number, PublishingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new RollbackItemCommand(siteId, collectionId, itemId, number, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpPost("copy-to-draft")]
    [RequireCapability(CmsCapabilities.ItemsEdit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyToDraftAsync(Guid siteId, Guid collectionId, Guid itemId, int number, RevisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new CopyVersionToDraftCommand(siteId, collectionId, itemId, number, request.Revision), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
