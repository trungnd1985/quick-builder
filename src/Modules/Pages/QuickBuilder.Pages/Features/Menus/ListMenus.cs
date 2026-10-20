using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Menus;

internal sealed record ListMenusQuery(Guid SiteId) : IQuery<Result<MenuListResponse>>;

/// <summary>A site's menus, by name, each with its counts and publication (024 FR-001).</summary>
internal sealed class ListMenusHandler(MenuReads reads, PagesAccess access) : IQueryHandler<ListMenusQuery, Result<MenuListResponse>>
{
    public async Task<Result<MenuListResponse>> HandleAsync(ListMenusQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuListResponse>(PagesErrors.SiteNotFound);
        }

        return Result.Success(await reads.ListAsync(query.SiteId, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: list a site's menus.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus")]
public sealed class ListMenusController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListMenusQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
