using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Menus;

/// <summary>One entry of a menu's draft, as the editor canvas draws it (024 FR-018, research NV-16).</summary>
/// <param name="Label">The entry's own label, else the page's current title or the item's current name; empty when neither.</param>
/// <param name="Kind"><c>page</c>, <c>item</c>, <c>url</c>, <c>anchor</c> or <c>none</c>.</param>
/// <param name="State">The target's state; <c>live</c> for entries without a page or item.</param>
public sealed record MenuPreviewEntry(
    string Id, string Label, string Kind, string State, bool Hidden, bool NewTab, IReadOnlyList<MenuPreviewEntry> Children);

/// <summary>A menu's draft resolved for the canvas. No addresses: links never navigate there.</summary>
public sealed record MenuPreviewResponse(Guid MenuId, string Name, IReadOnlyList<MenuPreviewEntry> Entries);

internal sealed record PreviewMenuQuery(Guid SiteId, Guid MenuId) : IQuery<Result<MenuPreviewResponse>>;

/// <summary>
/// The draft of a menu, with display labels and target states, for the Navigation block on the
/// editor canvas (024 FR-018). Hidden entries are included and flagged, so the canvas can show
/// them faded.
/// </summary>
internal sealed class PreviewMenuHandler(PagesDbContext database, PagesAccess access, MenuTargetStates targetStates)
    : IQueryHandler<PreviewMenuQuery, Result<MenuPreviewResponse>>
{
    public async Task<Result<MenuPreviewResponse>> HandleAsync(PreviewMenuQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuPreviewResponse>(PagesErrors.SiteNotFound);
        }

        var menu = await database.ForSite<Menu>(query.SiteId)
            .AsNoTracking()
            .Where(m => m.Id == query.MenuId)
            .Select(m => new
            {
                m.Name,
                Document = database.ForSite<MenuDraft>(query.SiteId).Where(d => d.MenuId == m.Id).Select(d => d.Document).First(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (menu is null)
        {
            return Result.Failure<MenuPreviewResponse>(PagesErrors.MenuNotFound);
        }

        var states = await targetStates
            .StatesAsync(query.SiteId, MenuTargets.Extract(menu.Document).Select(t => (t.Kind, t.Id)), cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(menu.Document);
        return Result.Success(new MenuPreviewResponse(query.MenuId, menu.Name, Entries(document.RootElement.GetProperty("entries"), states)));
    }

    private static List<MenuPreviewEntry> Entries(
        JsonElement entries, IReadOnlyDictionary<(string Kind, Guid Id), MenuTargetStateResponse> states) =>
        [
            .. entries.EnumerateArray().Select(entry =>
            {
                var target = entry.GetProperty("target");
                var kind = target.GetProperty("type").GetString()!;
                var state = kind switch
                {
                    "page" => states.GetValueOrDefault((MenuTargetKinds.Page, target.GetProperty("pageId").GetGuid())),
                    "item" => states.GetValueOrDefault((MenuTargetKinds.Item, target.GetProperty("itemId").GetGuid())),
                    _ => null,
                };
                var own = entry.TryGetProperty("label", out var label) ? label.GetString()?.Trim() : null;

                return new MenuPreviewEntry(
                    entry.GetProperty("id").GetString()!,
                    string.IsNullOrEmpty(own) ? state?.Title ?? string.Empty : own,
                    kind,
                    state?.State ?? MenuTargetStateNames.Live,
                    entry.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean(),
                    entry.TryGetProperty("newTab", out var newTab) && newTab.GetBoolean(),
                    Entries(entry.GetProperty("children"), states));
            }),
        ];
}

/// <summary>Administrative surface: a menu's draft for the editor canvas.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/preview")]
public sealed class PreviewMenuController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new PreviewMenuQuery(siteId, menuId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
