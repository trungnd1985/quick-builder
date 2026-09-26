using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.GetPageTree;

internal sealed record GetPageTreeQuery(Guid SiteId) : IQuery<Result<PageTreeResponse>>;

/// <summary>A site's whole page tree, flat and depth-first (FR-011, research P-16).</summary>
/// <remarks>An empty list means the site has no pages yet.</remarks>
internal sealed class GetPageTreeHandler(PageReads reads, PagesAccess access)
    : IQueryHandler<GetPageTreeQuery, Result<PageTreeResponse>>
{
    public async Task<Result<PageTreeResponse>> HandleAsync(GetPageTreeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageTreeResponse>(PagesErrors.SiteNotFound);
        }

        return Result.Success(await reads.TreeAsync(query.SiteId, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: a site's page tree.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages")]
public sealed class GetPageTreeController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetPageTreeQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
