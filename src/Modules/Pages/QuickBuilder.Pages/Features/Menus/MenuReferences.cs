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

/// <summary>A menu whose draft or live version points at a page or an item.</summary>
public sealed record MenuReferenceResponse(Guid MenuId, string Name, bool InDraft, bool InLive);

public sealed record MenuReferenceListResponse(IReadOnlyList<MenuReferenceResponse> Items);

internal sealed record MenuReferencesQuery(Guid SiteId, Guid? PageId, Guid? ItemId) : IQuery<Result<MenuReferenceListResponse>>;

/// <summary>
/// The menus that point at a page or an item (024 FR-026, research NV-19), for the warnings
/// before that page or item is deleted or unpublished: drafts from <c>menu_targets</c>, live
/// versions from the dependency graph.
/// </summary>
internal sealed class MenuReferencesHandler(PagesDbContext database, PagesAccess access)
    : IQueryHandler<MenuReferencesQuery, Result<MenuReferenceListResponse>>
{
    public async Task<Result<MenuReferenceListResponse>> HandleAsync(MenuReferencesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if ((query.PageId is null) == (query.ItemId is null))
        {
            return Result.Failure<MenuReferenceListResponse>(PagesErrors.InvalidReferenceQuery);
        }

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuReferenceListResponse>(PagesErrors.SiteNotFound);
        }

        var (targetKind, dependencyKind, id) = query.PageId is { } pageId
            ? (MenuTargetKinds.Page, DependencyKinds.Page, pageId)
            : (MenuTargetKinds.Item, DependencyKinds.Item, query.ItemId!.Value);

        var inDraft = await database.ForSite<MenuTarget>(query.SiteId)
            .Where(t => t.TargetKind == targetKind && t.TargetId == id)
            .Select(t => t.MenuId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var inLive = await database.ForSite<LiveDependency>(query.SiteId)
            .Where(d => d.SubjectKind == SubjectKinds.Menu && d.TargetKind == dependencyKind && d.TargetId == id)
            .Select(d => d.SubjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = inDraft.Union(inLive).ToList();
        var names = await database.ForSite<Menu>(query.SiteId)
            .Where(m => ids.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new MenuReferenceListResponse(
        [
            .. ids.Where(names.ContainsKey)
                .Select(menuId => new MenuReferenceResponse(menuId, names[menuId], inDraft.Contains(menuId), inLive.Contains(menuId)))
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        ]));
    }
}

/// <summary>Administrative surface: the menus that point at a page or an item.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/references")]
public sealed class MenuReferencesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReferencesAsync(Guid siteId, [FromQuery] Guid? pageId, [FromQuery] Guid? itemId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new MenuReferencesQuery(siteId, pageId, itemId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
