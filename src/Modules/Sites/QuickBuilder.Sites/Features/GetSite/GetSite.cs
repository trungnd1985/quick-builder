using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.GetSite;

internal sealed record GetSiteQuery(Guid SiteId) : IQuery<Result<SiteResponse>>;

internal sealed class GetSiteHandler(ISiteDirectory directory, IAccessEvaluator access)
    : IQueryHandler<GetSiteQuery, Result<SiteResponse>>
{
    public async Task<Result<SiteResponse>> HandleAsync(GetSiteQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var site = await directory.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false);
        if (site is null)
        {
            return Result.Failure<SiteResponse>(Error.NotFound("sites.not_found", "No such site."));
        }

        // Every status: a disabled site stays fully visible on the administrative surface
        // (009 FR-019).
        return Result.Success(await SiteResponses.BuildAsync(site, access, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>
/// Administrative surface. The site is named explicitly in the path and is never inferred
/// from the Host header (FR-011b).
/// </summary>
[ApiController]
[Route("api/v1/sites")]
public sealed class GetSiteController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Gets one site by id, with every setting.</summary>
    [HttpGet("{siteId:guid}")]
    [RequireCapability(SitesCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .QueryAsync(new GetSiteQuery(siteId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? ErrorResults.ToActionResult(result.Error)
            : Ok(result.Value);
    }
}
