using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Menus;

/// <summary>A page or component that shows a menu, in its draft, its live version, or both.</summary>
/// <param name="Kind"><c>page</c> or <c>component</c>.</param>
/// <param name="Label">The page's title or the component's name.</param>
public sealed record MenuUsageResponse(string Kind, Guid Id, string Label, bool InDraft, bool InLive);

public sealed record MenuUsageListResponse(IReadOnlyList<MenuUsageResponse> Items);

internal sealed record MenuUsagesQuery(Guid SiteId, Guid MenuId) : IQuery<Result<MenuUsageListResponse>>;

/// <summary>
/// Where a menu is shown (024 FR-003, research NV-06): drafts from <c>menu_usages</c>, live
/// versions from the dependency graph. The admin lists the drafts before a delete; the live
/// ones are why an unpublish or delete is refused.
/// </summary>
internal sealed class MenuUsagesHandler(PagesDbContext database, PagesAccess access) : IQueryHandler<MenuUsagesQuery, Result<MenuUsageListResponse>>
{
    public async Task<Result<MenuUsageListResponse>> HandleAsync(MenuUsagesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuUsageListResponse>(PagesErrors.SiteNotFound);
        }

        if (!await database.ForSite<Menu>(query.SiteId).AnyAsync(m => m.Id == query.MenuId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuUsageListResponse>(PagesErrors.MenuNotFound);
        }

        var inDraft = await database.ForSite<MenuUsage>(query.SiteId)
            .Where(u => u.MenuId == query.MenuId)
            .Select(u => new { u.SubjectKind, u.SubjectId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var inLive = await database.ForSite<LiveDependency>(query.SiteId)
            .Where(d => d.TargetKind == DependencyKinds.Menu && d.TargetId == query.MenuId)
            .Select(d => new { d.SubjectKind, d.SubjectId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var subjects = inDraft.Concat(inLive).Distinct().ToList();
        var pageIds = subjects.Where(s => s.SubjectKind == SubjectKinds.Page).Select(s => s.SubjectId).ToList();
        var componentIds = subjects.Where(s => s.SubjectKind == SubjectKinds.Component).Select(s => s.SubjectId).ToList();

        var pages = await database.ForSite<Page>(query.SiteId).Where(p => pageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken).ConfigureAwait(false);
        var components = await database.ForSite<Component>(query.SiteId).Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken).ConfigureAwait(false);

        return Result.Success(new MenuUsageListResponse(
        [
            .. subjects
                .Select(s => new MenuUsageResponse(
                    s.SubjectKind,
                    s.SubjectId,
                    (s.SubjectKind == SubjectKinds.Page ? pages.GetValueOrDefault(s.SubjectId) : components.GetValueOrDefault(s.SubjectId)) ?? string.Empty,
                    inDraft.Contains(s),
                    inLive.Contains(s)))
                .OrderBy(u => u.Kind, StringComparer.Ordinal)
                .ThenBy(u => u.Label, StringComparer.CurrentCultureIgnoreCase),
        ]));
    }
}

/// <summary>Administrative surface: the pages and components that show a menu.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/usages")]
public sealed class MenuUsagesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UsagesAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new MenuUsagesQuery(siteId, menuId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
