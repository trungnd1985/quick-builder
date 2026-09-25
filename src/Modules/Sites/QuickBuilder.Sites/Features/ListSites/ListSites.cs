using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.ListSites;

internal sealed record ListSitesQuery : IQuery<IReadOnlyList<SiteResponse>>;

internal sealed class ListSitesHandler(SitesDbContext database, IAccessEvaluator access, SiteDtoMapper mapper)
    : IQueryHandler<ListSitesQuery, IReadOnlyList<SiteResponse>>
{
    public async Task<IReadOnlyList<SiteResponse>> HandleAsync(ListSitesQuery query, CancellationToken cancellationToken)
    {
        // 002 FR-022: an administrator sees every site; anyone else sees only the sites
        // they hold a role on. Asked of the kernel's IAccessEvaluator, so this module never
        // references Identity (002 research R-05).
        var visibility = await access.GetVisibleSitesAsync(cancellationToken).ConfigureAwait(false);
        if (!visibility.All && visibility.SiteIds.Count == 0)
        {
            return [];
        }

        // Deliberate cross-site read (FR-013a). The admin surface has no resolved site, so
        // the ambient filter would match nothing — and listing the caller's sites is the
        // point of this screen. It goes through the annotated helper with a written reason,
        // which is the only way to escape the filter; anything else fails the build (D-07).
        var sites = database.Sites
            .AsNoTracking()
            .AcrossAllSites("The admin site list is cross-site by definition, narrowed to the caller's permitted sites; no site is resolved for admin requests.");

        if (!visibility.All)
        {
            var permitted = visibility.SiteIds.ToList();
            sites = sites.Where(s => permitted.Contains(s.Id));
        }

        // AsNoTracking + projection: a read path never returns entities (constitution
        // Technology Constraints). The mapper finishes what SQL cannot, such as the base URL.
        var rows = await sites
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(SiteDtoMapper.Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await SiteResponses
            .BuildAsync(rows.Select(mapper.ToDto), access, cancellationToken)
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
