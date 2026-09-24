using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.ListSites;

internal sealed record ListSitesQuery : IQuery<IReadOnlyList<SiteDto>>;

internal sealed class ListSitesHandler(SitesDbContext database)
    : IQueryHandler<ListSitesQuery, IReadOnlyList<SiteDto>>
{
    public async Task<IReadOnlyList<SiteDto>> HandleAsync(ListSitesQuery query, CancellationToken cancellationToken)
    {
        // Deliberate cross-site read (FR-013a). The admin surface has no resolved site, so
        // the ambient filter would match nothing — and listing every site is the point of
        // this screen. It goes through the annotated helper with a written reason, which
        // is the only way to escape the filter; anything else fails the build (D-07).
        //
        // AsNoTracking + projection to a DTO: a read path never returns entities
        // (constitution Technology Constraints).
        return await database.Sites
            .AsNoTracking()
            .AcrossAllSites("The admin site list is cross-site by definition; no site is resolved for admin requests.")
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SiteDto(
                s.Id,
                s.Name,
                s.HostNames.Select(h => h.HostName).ToList(),
                s.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Administrative surface. Returns an empty list on a fresh install (FR-014a).</summary>
[ApiController]
[Route("api/v1/sites")]
public sealed class ListSitesController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists every site, newest first.</summary>
    [HttpGet]
    [RequireSignedIn]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var sites = await dispatcher
            .QueryAsync(new ListSitesQuery(), cancellationToken)
            .ConfigureAwait(false);

        // An empty array is the normal state of a new installation, not an error.
        return Ok(sites);
    }
}
