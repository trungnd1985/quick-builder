using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.GetComponent;

internal sealed record GetComponentQuery(Guid SiteId, Guid ComponentId) : IQuery<Result<ComponentResponse>>;

/// <summary>One component. Another site's id answers exactly as an id that never existed.</summary>
internal sealed class GetComponentHandler(ComponentReads reads, PagesAccess access)
    : IQueryHandler<GetComponentQuery, Result<ComponentResponse>>
{
    public async Task<Result<ComponentResponse>> HandleAsync(GetComponentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ComponentResponse>(PagesErrors.SiteNotFound);
        }

        return await reads.OneAsync(query.SiteId, query.ComponentId, cancellationToken).ConfigureAwait(false) is { } component
            ? Result.Success(component)
            : Result.Failure<ComponentResponse>(PagesErrors.ComponentNotFound);
    }
}

/// <summary>Administrative surface: one component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}")]
public sealed class GetComponentController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetComponentQuery(siteId, componentId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
