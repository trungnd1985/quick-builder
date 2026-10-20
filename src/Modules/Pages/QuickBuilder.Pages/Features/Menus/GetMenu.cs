using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Menus;

internal sealed record GetMenuQuery(Guid SiteId, Guid MenuId) : IQuery<Result<MenuDetailResponse>>;

/// <summary>One menu with its draft and the state of every target (024 FR-025).</summary>
internal sealed class GetMenuHandler(MenuReads reads, PagesAccess access) : IQueryHandler<GetMenuQuery, Result<MenuDetailResponse>>
{
    public async Task<Result<MenuDetailResponse>> HandleAsync(GetMenuQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuDetailResponse>(PagesErrors.SiteNotFound);
        }

        return await reads.OneAsync(query.SiteId, query.MenuId, cancellationToken).ConfigureAwait(false) is { } menu
            ? Result.Success(menu)
            : Result.Failure<MenuDetailResponse>(PagesErrors.MenuNotFound);
    }
}

/// <summary>Administrative surface: one menu.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}")]
public sealed class GetMenuController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetMenuQuery(siteId, menuId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
