using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.GetComponentDraft;

internal sealed record GetComponentDraftQuery(Guid SiteId, Guid ComponentId) : IQuery<Result<DraftResponse>>;

/// <summary>A component's draft document (FR-020).</summary>
internal sealed class GetComponentDraftHandler(PagesDbContext database, PageReads reads, PagesAccess access)
    : IQueryHandler<GetComponentDraftQuery, Result<DraftResponse>>
{
    public async Task<Result<DraftResponse>> HandleAsync(GetComponentDraftQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DraftResponse>(PagesErrors.SiteNotFound);
        }

        var draft = await database.ForSite<ComponentDraft>(query.SiteId)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ComponentId == query.ComponentId, cancellationToken)
            .ConfigureAwait(false);

        return draft is null
            ? Result.Failure<DraftResponse>(PagesErrors.ComponentNotFound)
            : await reads.ReadDraftAsync(draft, SubjectKinds.Component, query.ComponentId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: read a component's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/draft")]
public sealed class GetComponentDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetComponentDraftQuery(siteId, componentId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
