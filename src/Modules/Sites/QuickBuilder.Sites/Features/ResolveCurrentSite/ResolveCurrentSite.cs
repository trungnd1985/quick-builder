using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Sites.Features.ResolveCurrentSite;

/// <summary>What the public placeholder returns.</summary>
public sealed record CurrentSiteResponse(Guid SiteId, string SiteName);

internal sealed record CurrentSiteQuery : IQuery<Result<CurrentSiteResponse>>;

/// <summary>
/// Reports the site the request resolved to.
/// </summary>
/// <remarks>
/// Reads the ambient site rather than the Host header (FR-011a): middleware has already
/// decided which site this request belongs to, and a handler that re-derived it could
/// disagree with the filter applied to its own queries.
/// </remarks>
internal sealed class CurrentSiteHandler(ISiteContext siteContext, ISiteDirectory directory)
    : IQueryHandler<CurrentSiteQuery, Result<CurrentSiteResponse>>
{
    public async Task<Result<CurrentSiteResponse>> HandleAsync(
        CurrentSiteQuery query,
        CancellationToken cancellationToken)
    {
        if (!siteContext.HasSite)
        {
            // Unreachable in practice: middleware rejects unresolved public requests
            // before routing. Kept as a guard so the invariant is stated rather than
            // assumed, and so a future routing change fails loudly rather than leaking.
            return Result.Failure<CurrentSiteResponse>(
                Error.NotFound("sites.not_resolved", "No site was resolved for this request."));
        }

        var site = await directory
            .FindByIdAsync(siteContext.SiteId!.Value, cancellationToken)
            .ConfigureAwait(false);

        return site is null
            ? Result.Failure<CurrentSiteResponse>(
                Error.NotFound("sites.not_resolved", "No site was resolved for this request."))
            : Result.Success(new CurrentSiteResponse(site.Id, site.Name));
    }
}

/// <summary>
/// The entire public surface in this feature.
/// </summary>
/// <remarks>
/// Exists so that site resolution is demonstrable end to end (US1 scenario 3, SC-001a):
/// a request arrives with a Host header, middleware resolves it to a site before this
/// action runs, and the action reports which site it got. The Rendering module replaces
/// this later with actual pages.
/// <para>
/// It goes through the dispatcher like every other slice even though the work is trivial.
/// This is the public-surface example a later module copies, and a controller that reached
/// for a service directly "because it is only one call" is exactly how the convention
/// erodes — the version before this one did that, and a conventions test caught it.
/// </para>
/// </remarks>
[ApiController]
// Public surface: site visitors never authenticate (002 contracts/authorization.md §4).
[AllowAnonymous]
[Route("/")]
public sealed class ResolveCurrentSiteController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Reports the site the Host header resolved to.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .QueryAsync(new CurrentSiteQuery(), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? ErrorResults.ToActionResult(result.Error)
            : Ok(result.Value);
    }
}
