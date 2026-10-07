using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.GetCollection;

internal sealed record GetCollectionQuery(Guid SiteId, Guid CollectionId) : IQuery<Result<CollectionResponse>>;

/// <summary>One collection: details, schema, revision and item count (FR-013).</summary>
internal sealed class GetCollectionHandler(CollectionReads reads, CmsAccess access)
    : IQueryHandler<GetCollectionQuery, Result<CollectionResponse>>
{
    public async Task<Result<CollectionResponse>> HandleAsync(GetCollectionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<CollectionResponse>(CmsErrors.SiteNotFound);
        }

        var collection = await reads.OneAsync(query.SiteId, query.CollectionId, cancellationToken).ConfigureAwait(false);
        return collection is null ? Result.Failure<CollectionResponse>(CmsErrors.CollectionNotFound) : Result.Success(collection);
    }
}

/// <summary>Administrative surface: one collection.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}")]
public sealed class GetCollectionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetCollectionQuery(siteId, collectionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
