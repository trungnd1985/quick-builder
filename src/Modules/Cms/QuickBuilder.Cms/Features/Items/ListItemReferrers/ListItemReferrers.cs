using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.ListItemReferrers;

internal sealed record ListItemReferrersQuery(Guid SiteId, Guid CollectionId, Guid ItemId, bool PublishedOnly) : IQuery<Result<ReferrerListResponse>>;

/// <summary>The items that refer to an item (015 FR-008, FR-021; research IT-10).</summary>
internal sealed class ListItemReferrersHandler(ItemReferrers referrers, CmsDbContext database, CmsAccess access)
    : IQueryHandler<ListItemReferrersQuery, Result<ReferrerListResponse>>
{
    public async Task<Result<ReferrerListResponse>> HandleAsync(ListItemReferrersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ReferrerListResponse>(CmsErrors.SiteNotFound);
        }

        if (!await database.ForSite<Item>(query.SiteId).AnyAsync(i => i.Id == query.ItemId && i.CollectionId == query.CollectionId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ReferrerListResponse>(CmsErrors.ItemNotFound);
        }

        return Result.Success(await referrers.ListAsync(query.SiteId, query.ItemId, query.PublishedOnly, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: the items that refer to an item.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/referrers")]
public sealed class ListItemReferrersController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, Guid collectionId, Guid itemId, [FromQuery] bool? published, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListItemReferrersQuery(siteId, collectionId, itemId, published == true), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
