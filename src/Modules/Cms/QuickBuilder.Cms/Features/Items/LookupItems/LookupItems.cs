using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.LookupItems;

internal sealed record LookupItemsQuery(Guid SiteId, IReadOnlyList<Guid> Ids) : IQuery<Result<ItemLookupResponse>>;

/// <summary>
/// Names and statuses of items by id, across the site's collections (015 US4, research IT-18):
/// what reference chips show. Ids the site does not hold, including another site's, are missing.
/// </summary>
internal sealed class LookupItemsHandler(CmsDbContext database, CmsAccess access) : IQueryHandler<LookupItemsQuery, Result<ItemLookupResponse>>
{
    public const int MaxIds = 200;

    public async Task<Result<ItemLookupResponse>> HandleAsync(LookupItemsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemLookupResponse>(CmsErrors.SiteNotFound);
        }

        if (query.Ids.Count > MaxIds)
        {
            return Result.Failure<ItemLookupResponse>(CmsErrors.InvalidQuery("ids"));
        }

        var ids = query.Ids.Distinct().ToList();
        var found = await database.ForSite<Item>(query.SiteId)
            .AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new ItemRefResponse(i.Id, i.CollectionId, i.Name, i.Slug, i.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var known = found.Select(i => i.Id).ToHashSet();
        return Result.Success(new ItemLookupResponse(found, ids.Where(id => !known.Contains(id)).ToList()));
    }
}

/// <summary>Administrative surface: look items up by id.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/items/lookup")]
public sealed class LookupItemsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LookupAsync(Guid siteId, LookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.QueryAsync(new LookupItemsQuery(siteId, request.Ids ?? []), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
