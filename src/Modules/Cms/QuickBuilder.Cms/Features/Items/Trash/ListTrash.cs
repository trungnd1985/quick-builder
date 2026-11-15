using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.Trash;

internal sealed record ListTrashQuery(Guid SiteId, TrashQueryOptions Options) : IQuery<Result<TrashListResponse>>;

internal sealed record GetTrashEntryQuery(Guid SiteId, Guid ItemId) : IQuery<Result<TrashEntryDetailResponse>>;

/// <summary>A site's trashed items, newest first (021 FR-009, FR-010), and one with its draft (FR-011).</summary>
internal sealed class ListTrashHandler(CmsTrash trash, CmsAccess access)
    : IQueryHandler<ListTrashQuery, Result<TrashListResponse>>, IQueryHandler<GetTrashEntryQuery, Result<TrashEntryDetailResponse>>
{
    public async Task<Result<TrashListResponse>> HandleAsync(ListTrashQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return !await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false)
            ? Result.Failure<TrashListResponse>(CmsErrors.SiteNotFound)
            : Result.Success(await trash.ListAsync(query.SiteId, query.Options, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<TrashEntryDetailResponse>> HandleAsync(GetTrashEntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return !await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false)
            ? Result.Failure<TrashEntryDetailResponse>(CmsErrors.SiteNotFound)
            : await trash.GetAsync(query.SiteId, query.ItemId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: the item trash (trash-api.yaml).</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/trash/items")]
public sealed class ListTrashController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Trashed items, newest first.</summary>
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType<TrashListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        Guid siteId, [FromQuery] string? q, [FromQuery] Guid? trashedBy, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? collectionId, [FromQuery] string? after, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var options = new TrashQueryOptions(q, trashedBy, from, to, collectionId, after, limit ?? CmsTrash.PageSize);
        var result = await dispatcher.QueryAsync(new ListTrashQuery(siteId, options), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>One trashed item, with its draft values.</summary>
    [HttpGet("{itemId:guid}")]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType<TrashEntryDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetTrashEntryQuery(siteId, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
