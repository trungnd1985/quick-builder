using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Trash;

internal sealed record ListTrashQuery(Guid SiteId, string Kind, TrashQueryOptions Options) : IQuery<Result<TrashListResponse>>;

internal sealed record GetTrashEntryQuery(Guid SiteId, string Kind, Guid Id) : IQuery<Result<TrashEntryDetailResponse>>;

/// <summary>
/// A site's trashed pages or components, newest first, with search and filters (021 FR-009,
/// FR-010), and one entry with its draft for the read-only preview (FR-011).
/// </summary>
internal sealed class ListTrashHandler(PagesTrash trash, PagesAccess access)
    : IQueryHandler<ListTrashQuery, Result<TrashListResponse>>, IQueryHandler<GetTrashEntryQuery, Result<TrashEntryDetailResponse>>
{
    public async Task<Result<TrashListResponse>> HandleAsync(ListTrashQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<TrashListResponse>(PagesErrors.SiteNotFound);
        }

        return await trash.ListAsync(query.SiteId, query.Kind, query.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TrashEntryDetailResponse>> HandleAsync(GetTrashEntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<TrashEntryDetailResponse>(PagesErrors.SiteNotFound);
        }

        return await trash.GetAsync(query.SiteId, query.Kind, query.Id, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: the Pages trash (trash-api.yaml).</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/trash")]
public sealed class ListTrashController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Trashed pages, newest first.</summary>
    [HttpGet("pages")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType<TrashListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ListPagesAsync(
        Guid siteId, [FromQuery] string? q, [FromQuery] Guid? trashedBy, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? after, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        ListAsync(siteId, TrashKinds.Page, new TrashQueryOptions(q, trashedBy, from, to, after, limit ?? PagesTrash.PageSize), cancellationToken);

    /// <summary>Trashed components, newest first.</summary>
    [HttpGet("components")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType<TrashListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ListComponentsAsync(
        Guid siteId, [FromQuery] string? q, [FromQuery] Guid? trashedBy, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? after, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        ListAsync(siteId, TrashKinds.Component, new TrashQueryOptions(q, trashedBy, from, to, after, limit ?? PagesTrash.PageSize), cancellationToken);

    /// <summary>One trashed page, with its draft.</summary>
    [HttpGet("pages/{pageId:guid}")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType<TrashEntryDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetPageAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken) =>
        GetAsync(siteId, TrashKinds.Page, pageId, cancellationToken);

    /// <summary>One trashed component, with its draft.</summary>
    [HttpGet("components/{componentId:guid}")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType<TrashEntryDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetComponentAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken) =>
        GetAsync(siteId, TrashKinds.Component, componentId, cancellationToken);

    private async Task<IActionResult> ListAsync(Guid siteId, string kind, TrashQueryOptions options, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListTrashQuery(siteId, kind, options), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    private async Task<IActionResult> GetAsync(Guid siteId, string kind, Guid id, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetTrashEntryQuery(siteId, kind, id), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
