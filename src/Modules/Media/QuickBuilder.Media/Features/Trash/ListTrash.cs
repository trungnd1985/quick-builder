using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.Trash;

internal sealed record ListTrashQuery(Guid SiteId, TrashQueryOptions Options) : IQuery<Result<TrashListResponse>>;

internal sealed record GetTrashEntryQuery(Guid SiteId, Guid FileId) : IQuery<Result<TrashEntryResponse>>;

/// <summary>A site's trashed files, newest first, with their total size (021 FR-009, FR-010, FR-012).</summary>
internal sealed class ListTrashHandler(MediaTrash trash, ISiteDirectory sites)
    : IQueryHandler<ListTrashQuery, Result<TrashListResponse>>, IQueryHandler<GetTrashEntryQuery, Result<TrashEntryResponse>>
{
    public async Task<Result<TrashListResponse>> HandleAsync(ListTrashQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null
            ? Result.Failure<TrashListResponse>(MediaErrors.SiteNotFound)
            : Result.Success(await trash.ListAsync(query.SiteId, query.Options, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<TrashEntryResponse>> HandleAsync(GetTrashEntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<TrashEntryResponse>(MediaErrors.SiteNotFound);
        }

        var file = await trash.TrashedFiles(query.SiteId).AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.FileId, cancellationToken).ConfigureAwait(false);
        return file is null
            ? Result.Failure<TrashEntryResponse>(MediaErrors.NotInTrash)
            : Result.Success((await trash.DescribeAsync(query.SiteId, [file], cancellationToken).ConfigureAwait(false))[0]);
    }
}

/// <summary>Administrative surface: the media trash (trash-api.yaml).</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/trash/files")]
public sealed class ListTrashController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Trashed files, newest first, with the storage they take.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType<TrashListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        Guid siteId, [FromQuery] string? q, [FromQuery] Guid? trashedBy, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? folderId, [FromQuery] string? after, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var options = new TrashQueryOptions(q, trashedBy, from, to, folderId, after, limit ?? MediaTrash.PageSize);
        var result = await dispatcher.QueryAsync(new ListTrashQuery(siteId, options), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>One trashed file, for its preview (FR-011).</summary>
    [HttpGet("{fileId:guid}")]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType<TrashEntryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetTrashEntryQuery(siteId, fileId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
