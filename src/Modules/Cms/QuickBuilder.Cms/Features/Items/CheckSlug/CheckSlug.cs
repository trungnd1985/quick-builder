using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.Items.CheckSlug;

internal sealed record CheckSlugQuery(Guid SiteId, Guid CollectionId, string? Slug, Guid? ItemId) : IQuery<Result<SlugAvailabilityResponse>>;

/// <summary>
/// Whether a slug is free in a collection, and the slug to suggest (015 research IT-03). Advisory:
/// the form asks as the member types; only the save decides.
/// </summary>
internal sealed class CheckSlugHandler(CmsDbContext database, SlugAvailability slugs, CmsAccess access)
    : IQueryHandler<CheckSlugQuery, Result<SlugAvailabilityResponse>>
{
    public async Task<Result<SlugAvailabilityResponse>> HandleAsync(CheckSlugQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<SlugAvailabilityResponse>(CmsErrors.SiteNotFound);
        }

        if (!await database.ForSite<Collection>(query.SiteId).AnyAsync(c => c.Id == query.CollectionId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<SlugAvailabilityResponse>(CmsErrors.CollectionNotFound);
        }

        // A slug that breaks the grammar is not available; the suggestion is built from it.
        var slug = query.Slug?.Trim() ?? string.Empty;
        var wanted = SlugRules.IsValid(slug) ? slug : SlugRules.FromName(slug);
        if (wanted.Length == 0)
        {
            return Result.Success(new SlugAvailabilityResponse(false, string.Empty));
        }

        var free = await slugs.NextFreeAsync(query.SiteId, query.CollectionId, wanted, query.ItemId, cancellationToken).ConfigureAwait(false);
        return Result.Success(new SlugAvailabilityResponse(string.Equals(free, slug, StringComparison.Ordinal), free));
    }
}

/// <summary>Administrative surface: is a slug free?</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/slug-availability")]
public sealed class CheckSlugController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckAsync(Guid siteId, Guid collectionId, [FromQuery] string? slug, [FromQuery] Guid? itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new CheckSlugQuery(siteId, collectionId, slug, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
