using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.ListComponents;

internal sealed record ListComponentsQuery(Guid SiteId) : IQuery<Result<ComponentListResponse>>;

/// <summary>A site's shared components, by name, each with its usage count (FR-025, FR-028).</summary>
internal sealed class ListComponentsHandler(ComponentReads reads, PagesAccess access)
    : IQueryHandler<ListComponentsQuery, Result<ComponentListResponse>>
{
    public async Task<Result<ComponentListResponse>> HandleAsync(ListComponentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ComponentListResponse>(PagesErrors.SiteNotFound);
        }

        return Result.Success(await reads.ListAsync(query.SiteId, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: list a site's components.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components")]
public sealed class ListComponentsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListComponentsQuery(siteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
