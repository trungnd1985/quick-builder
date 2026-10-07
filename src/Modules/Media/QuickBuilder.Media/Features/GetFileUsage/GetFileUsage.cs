using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Media;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Media.Features.GetFileUsage;

/// <summary>
/// One page, component or collection item that uses a file (contracts/media-api.yaml
/// <c>Usage.places</c>). Items (015) add their collection; <c>InScheduled</c> says a pending
/// scheduled publish shows it.
/// </summary>
internal sealed record MediaUsagePlaceDto(
    string Kind,
    Guid Id,
    string Title,
    bool InDraft,
    bool Live,
    bool InHistory,
    int? LivePagesThroughComponent,
    Guid? CollectionId = null,
    string? CollectionName = null,
    bool InScheduled = false);

/// <summary>Where a file is used (contracts/media-api.yaml <c>Usage</c>).</summary>
internal sealed record MediaUsageDto(MediaUsageSummaryDto Summary, IReadOnlyList<MediaUsagePlaceDto> Places);

internal sealed record GetFileUsageQuery(Guid SiteId, Guid FileId) : IQuery<Result<MediaUsageDto>>;

/// <summary>
/// Where a file is used (013 FR-014). Counts for anyone who may see the library; the pages and
/// components themselves only for those who may see page content, and the items (015 IT-15)
/// only for those who may see collections.
/// </summary>
internal sealed class GetFileUsageHandler(
    MediaDbContext database,
    ISiteDirectory sites,
    IAccessEvaluator access,
    MediaUsage usage) : IQueryHandler<GetFileUsageQuery, Result<MediaUsageDto>>
{
    /// <summary>Cms's capability to see collections and their items (015 IT-16).</summary>
    private const string CollectionsView = "cms.collections.view";

    public async Task<Result<MediaUsageDto>> HandleAsync(GetFileUsageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await sites.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<MediaUsageDto>(MediaErrors.SiteNotFound);
        }

        var exists = await database.MediaFiles
            .AcrossAllSites("Admin read of a file on a site named explicitly in the route (001 FR-011b).")
            .AnyAsync(f => f.Id == query.FileId && f.SiteId == query.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return Result.Failure<MediaUsageDto>(MediaErrors.NotFound);
        }

        var summary = (await usage.SummariseAsync(query.SiteId, [query.FileId], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(query.FileId) ?? MediaUsageSummary.None;

        var places = new List<MediaUsagePlaceDto>();
        if (await access.HasCapabilityAsync(PageContentCapabilities.View, query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            places.AddRange((await usage.PagePlacesAsync(query.SiteId, query.FileId, cancellationToken).ConfigureAwait(false))
                .Select(p => new MediaUsagePlaceDto(p.Kind, p.Id, p.Title, p.InDraft, p.Live, p.InHistory, p.LivePagesThroughComponent)));
        }

        // The Cms capability is named, not referenced: Media reads Cms only through its contracts.
        if (await access.HasCapabilityAsync(CollectionsView, query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            places.AddRange((await usage.ItemPlacesAsync(query.SiteId, query.FileId, cancellationToken).ConfigureAwait(false))
                .Select(p => new MediaUsagePlaceDto(
                    "item", p.ItemId, p.Name ?? p.Slug, p.InDraft, p.Live, p.InHistory, null, p.CollectionId, p.CollectionName, p.InScheduled)));
        }

        return Result.Success(new MediaUsageDto(MediaFileMapper.ToDto(summary), places));
    }
}

/// <summary>Administrative surface: where a file is used, before deleting or replacing it.</summary>
[ApiController]
[Route("api/v1/media/sites/{siteId:guid}/files/{fileId:guid}/usage")]
public sealed class GetFileUsageController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>The usage counts, and the pages and components when the caller may see page content.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid fileId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetFileUsageQuery(siteId, fileId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
