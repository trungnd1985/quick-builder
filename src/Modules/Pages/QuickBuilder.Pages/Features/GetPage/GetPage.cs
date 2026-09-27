using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.GetPage;

internal sealed record GetPageQuery(Guid SiteId, Guid PageId) : IQuery<Result<PageDetailResponse>>;

/// <summary>
/// One page with its draft's metadata (FR-011). Another site's page id answers exactly as
/// an id that never existed.
/// </summary>
internal sealed class GetPageHandler(PageReads reads, PagesAccess access)
    : IQueryHandler<GetPageQuery, Result<PageDetailResponse>>
{
    public async Task<Result<PageDetailResponse>> HandleAsync(GetPageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageDetailResponse>(PagesErrors.SiteNotFound);
        }

        return await reads.PageAsync(query.SiteId, query.PageId, cancellationToken).ConfigureAwait(false) is { } page
            ? Result.Success(page)
            : Result.Failure<PageDetailResponse>(PagesErrors.NotFound);
    }
}

/// <summary>Administrative surface: one page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}")]
public sealed class GetPageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetPageQuery(siteId, pageId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
