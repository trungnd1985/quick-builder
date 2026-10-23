using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.GetItem;

internal sealed record GetItemQuery(Guid SiteId, Guid CollectionId, Guid ItemId) : IQuery<Result<ItemResponse>>;

/// <summary>One item: draft, published values, report, schedules (015 contracts/items-api.yaml).</summary>
internal sealed class GetItemHandler(ItemReads reads, CmsAccess access) : IQueryHandler<GetItemQuery, Result<ItemResponse>>
{
    public async Task<Result<ItemResponse>> HandleAsync(GetItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemResponse>(CmsErrors.SiteNotFound);
        }

        return await reads.ReadAsync(query.SiteId, query.CollectionId, query.ItemId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: one item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}")]
public sealed class GetItemController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid collectionId, Guid itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetItemQuery(siteId, collectionId, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
