using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.ListComponentUsages;

internal sealed record ListComponentUsagesQuery(Guid SiteId, Guid ComponentId) : IQuery<Result<IReadOnlyList<PageReferenceResponse>>>;

/// <summary>The pages whose drafts reference a component, by address (FR-028).</summary>
internal sealed class ListComponentUsagesHandler(PagesDbContext database, PagesAccess access)
    : IQueryHandler<ListComponentUsagesQuery, Result<IReadOnlyList<PageReferenceResponse>>>
{
    public async Task<Result<IReadOnlyList<PageReferenceResponse>>> HandleAsync(
        ListComponentUsagesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<IReadOnlyList<PageReferenceResponse>>(PagesErrors.SiteNotFound);
        }

        if (!await database.ForSite<Component>(query.SiteId).AnyAsync(c => c.Id == query.ComponentId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<IReadOnlyList<PageReferenceResponse>>(PagesErrors.ComponentNotFound);
        }

        var pages = await database.ForSite<ComponentUsage>(query.SiteId)
            .Where(u => u.ComponentId == query.ComponentId)
            .Join(database.ForSite<Page>(query.SiteId), u => u.PageId, p => p.Id, (_, p) => p)
            .AsNoTracking()
            .OrderBy(p => p.Path)
            .Select(p => new PageReferenceResponse(p.Id, p.Title, p.Path))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<PageReferenceResponse>>(pages);
    }
}

/// <summary>Administrative surface: which pages use a component.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/usages")]
public sealed class ListComponentUsagesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListComponentUsagesQuery(siteId, componentId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
