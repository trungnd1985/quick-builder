using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.PreviewCollection;

internal sealed record PreviewCollectionQuery(Guid SiteId, Guid CollectionId, SaveCollectionRequest Request)
    : IQuery<Result<PreviewResponse>>;

/// <summary>
/// What saving a proposal would do to the stored items: counts per change, examples, and the
/// digest a confirmation must carry (research CS-08, FR-023).
/// </summary>
/// <remarks>
/// Read-only and takes no lock, so it never blocks a save; the save recomputes the same answer
/// under the lock. Validation errors are the save's.
/// </remarks>
internal sealed class PreviewCollectionHandler(CmsDbContext database, ProposalEvaluator evaluator, CmsAccess access, ICollectionUsageReader pageUses)
    : IQueryHandler<PreviewCollectionQuery, Result<PreviewResponse>>
{
    public async Task<Result<PreviewResponse>> HandleAsync(PreviewCollectionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PreviewResponse>(CmsErrors.SiteNotFound);
        }

        var collection = await database.ForSite<Collection>(query.SiteId)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == query.CollectionId, cancellationToken)
            .ConfigureAwait(false);

        if (collection is null)
        {
            return Result.Failure<PreviewResponse>(CmsErrors.CollectionNotFound);
        }

        if (query.Request.Revision != collection.Revision)
        {
            return Result.Failure<PreviewResponse>(CmsErrors.RevisionConflict(collection.Revision));
        }

        var evaluated = await evaluator.EvaluateAsync(collection, query.Request, cancellationToken).ConfigureAwait(false);
        if (evaluated.IsFailure)
        {
            return Result.Failure<PreviewResponse>(evaluated.Error);
        }

        // 016 FR-011: what the change would break on pages, counted across drafts, live and scheduled.
        var impact = evaluated.Value.Impact;
        var breaking = PreviewResponse.FieldsBreakingBindings(impact);
        var bound = breaking.Count == 0
            ? new Dictionary<string, int>()
            : await pageUses.CountFieldUsesAsync(query.SiteId, collection.Id, breaking, cancellationToken).ConfigureAwait(false);
        return Result.Success(PreviewResponse.From(collection.Revision, impact) with { BoundPages = bound });
    }
}

/// <summary>Administrative surface: preview a save.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/preview")]
public sealed class PreviewCollectionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(CmsCapabilities.CollectionsView)]
    [RequestSizeLimit(262_144)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PreviewAsync(Guid siteId, Guid collectionId, SaveCollectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.QueryAsync(new PreviewCollectionQuery(siteId, collectionId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
