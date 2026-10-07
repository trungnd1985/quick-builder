using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.ListCollections;

internal sealed record ListCollectionsQuery(Guid SiteId) : IQuery<Result<CollectionListResponse>>;

/// <summary>The site's collections by name, with field and item counts (FR-003).</summary>
internal sealed class ListCollectionsHandler(CollectionReads reads, CmsAccess access)
    : IQueryHandler<ListCollectionsQuery, Result<CollectionListResponse>>
{
    public async Task<Result<CollectionListResponse>> HandleAsync(ListCollectionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<CollectionListResponse>(CmsErrors.SiteNotFound);
        }

        return Result.Success(await reads.ListAsync(query.SiteId, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: list a site's collections.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections")]
public sealed class ListCollectionsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListCollectionsQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
