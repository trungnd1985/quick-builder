using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.GetItemHistory;

internal sealed record GetItemHistoryQuery(Guid SiteId, Guid CollectionId, Guid ItemId) : IQuery<Result<ItemHistoryResponse>>;

/// <summary>A kept version (015 FR-022, FR-023).</summary>
/// <param name="Slug">The slug this version published, so a rollback can say whether the address moves (017 FR-028).</param>
public sealed record VersionResponse(int Number, string Kind, int? RestoredFrom, DateTimeOffset PublishedAtUtc, MemberResponse PublishedBy, bool Live, string Slug);

/// <summary>Something that happened to the item (015 FR-031). No values, names or slugs.</summary>
public sealed record HistoryEventResponse(
    string Kind,
    int? VersionNumber,
    string? ScheduleAction,
    DateTimeOffset? DueAtUtc,
    string? FailureCode,
    DateTimeOffset AtUtc,
    MemberResponse By);

/// <summary>An item's kept versions and events, newest first.</summary>
public sealed record ItemHistoryResponse(IReadOnlyList<VersionResponse> Versions, IReadOnlyList<HistoryEventResponse> Events);

/// <summary>An item's history (015 US7, FR-031): its kept versions, the live one marked, and its events.</summary>
internal sealed class GetItemHistoryHandler(CmsDbContext database, CmsAccess access) : IQueryHandler<GetItemHistoryQuery, Result<ItemHistoryResponse>>
{
    public async Task<Result<ItemHistoryResponse>> HandleAsync(GetItemHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ItemHistoryResponse>(CmsErrors.SiteNotFound);
        }

        var item = await database.ForSite<Item>(query.SiteId)
            .AsNoTracking()
            .Where(i => i.Id == query.ItemId && i.CollectionId == query.CollectionId)
            .Select(i => new { i.LiveVersion })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<ItemHistoryResponse>(CmsErrors.ItemNotFound);
        }

        var versions = await database.ForSite<ItemVersion>(query.SiteId)
            .AsNoTracking()
            .Where(v => v.ItemId == query.ItemId)
            .OrderByDescending(v => v.Number)
            .Select(v => new { v.Number, v.Kind, v.RestoredFrom, v.PublishedAtUtc, v.PublishedBy, v.Slug })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var events = await database.ForSite<ItemHistoryEntry>(query.SiteId)
            .AsNoTracking()
            .Where(e => e.ItemId == query.ItemId)
            .OrderByDescending(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await access.NamesAsync(versions.Select(v => v.PublishedBy).Concat(events.Select(e => e.By)), cancellationToken).ConfigureAwait(false);
        MemberResponse Member(Guid id) => new(id, names.GetValueOrDefault(id, string.Empty));

        return Result.Success(new ItemHistoryResponse(
            versions.Select(v => new VersionResponse(v.Number, v.Kind, v.RestoredFrom, v.PublishedAtUtc, Member(v.PublishedBy), v.Number == item.LiveVersion, v.Slug)).ToList(),
            events.Select(e => new HistoryEventResponse(e.Kind, e.VersionNumber, e.ScheduleAction, e.DueAtUtc, e.FailureCode, e.AtUtc, Member(e.By))).ToList()));
    }
}

/// <summary>Administrative surface: an item's history.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/history")]
public sealed class GetItemHistoryController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid collectionId, Guid itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetItemHistoryQuery(siteId, collectionId, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
