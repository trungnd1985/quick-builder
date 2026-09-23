using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Sites.Features.GetSite;

internal sealed record GetSiteQuery(Guid SiteId) : IQuery<Result<SiteDto>>;

internal sealed class GetSiteHandler(ISiteDirectory directory) : IQueryHandler<GetSiteQuery, Result<SiteDto>>
{
    public async Task<Result<SiteDto>> HandleAsync(GetSiteQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var site = await directory.FindByIdAsync(query.SiteId, cancellationToken).ConfigureAwait(false);

        return site is null
            ? Result.Failure<SiteDto>(Error.NotFound("sites.not_found", "No such site."))
            : Result.Success(site);
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
    /// <summary>Gets one site by id.</summary>
    [HttpGet("{siteId:guid}")]
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
