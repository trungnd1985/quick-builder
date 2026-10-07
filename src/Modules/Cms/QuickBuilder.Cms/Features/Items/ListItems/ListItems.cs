using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.ListItems;

internal sealed record ListItemsQuery(Guid SiteId, Guid CollectionId, ItemListRequest Request) : IQuery<Result<ItemListResponse>>;

/// <summary>One page of a collection's items (015 US3, FR-028 to FR-030; research IT-13, IT-14).</summary>
internal sealed class ListItemsHandler(CmsDbContext database, ItemListQuery list, ItemReads reads, CmsAccess access)
    : IQueryHandler<ListItemsQuery, Result<ItemListResponse>>
{
    public async Task<Result<ItemListResponse>> HandleAsync(ListItemsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemListResponse>(CmsErrors.SiteNotFound);
        }

        var schema = await database.ForSite<Collection>(query.SiteId)
            .AsNoTracking()
            .Where(c => c.Id == query.CollectionId)
            .Select(c => c.Schema)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (schema is null)
        {
            return Result.Failure<ItemListResponse>(CmsErrors.CollectionNotFound);
        }

        var page = await list.RunAsync(query.SiteId, query.CollectionId, CollectionSchemaDocument.Read(schema), query.Request, cancellationToken)
            .ConfigureAwait(false);
        if (page.IsFailure)
        {
            return Result.Failure<ItemListResponse>(page.Error);
        }

        var summaries = await reads.SummariesAsync(query.SiteId, page.Value.Rows, cancellationToken).ConfigureAwait(false);
        return Result.Success(new ItemListResponse(summaries, page.Value.Total, page.Value.Page, page.Value.PageSize));
    }
}

/// <summary>Administrative surface: list, search, filter and sort a collection's items.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items")]
public sealed partial class ListItemsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        Guid siteId,
        Guid collectionId,
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] bool? attention,
        [FromQuery] string? scheduled,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        // Field filters arrive as field[<fieldId>]=<value> (contracts/items-api.yaml, deepObject).
        var fields = Request.Query
            .Select(p => (Match: FieldFilter().Match(p.Key), Value: p.Value.ToString()))
            .Where(p => p.Match.Success)
            .ToDictionary(p => p.Match.Groups[1].Value, p => p.Value, StringComparer.Ordinal);

        var request = new ItemListRequest(q, status, attention, scheduled, fields, sort, dir, page, pageSize);
        var result = await dispatcher.QueryAsync(new ListItemsQuery(siteId, collectionId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [GeneratedRegex(@"^field\[([^\]]{1,16})\]$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldFilter();
}
