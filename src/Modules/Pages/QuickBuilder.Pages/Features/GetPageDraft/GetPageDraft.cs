using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.GetPageDraft;

internal sealed record GetPageDraftQuery(Guid SiteId, Guid PageId) : IQuery<Result<DraftResponse>>;

/// <summary>A page's draft document, equal as JSON to what was saved (FR-020).</summary>
internal sealed class GetPageDraftHandler(PagesDbContext database, PageReads reads, PagesAccess access)
    : IQueryHandler<GetPageDraftQuery, Result<DraftResponse>>
{
    public async Task<Result<DraftResponse>> HandleAsync(GetPageDraftQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DraftResponse>(PagesErrors.SiteNotFound);
        }

        var draft = await database.ForSite<PageDraft>(query.SiteId)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.PageId == query.PageId, cancellationToken)
            .ConfigureAwait(false);

        return draft is null
            ? Result.Failure<DraftResponse>(PagesErrors.NotFound)
            : await reads.ReadDraftAsync(draft, SubjectKinds.Page, query.PageId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Administrative surface: read a page's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/draft")]
public sealed class GetPageDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetPageDraftQuery(siteId, pageId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
