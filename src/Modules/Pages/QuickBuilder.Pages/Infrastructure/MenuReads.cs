using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Features.Menus;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>The read side of menus (024 contracts/menus-api.yaml).</summary>
/// <remarks>
/// A site holds at most 50 menus, so the list reads every draft and counts in memory; the queries
/// are per table, not per menu.
/// </remarks>
internal sealed class MenuReads(PagesDbContext database, PagesAccess access, PublicationReads publications, MenuTargetStates targetStates, Schema.StoredDocuments stored)
{
    /// <summary>The site's menus, by name.</summary>
    public async Task<MenuListResponse> ListAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var summaries = await SummariesAsync(siteId, menuId: null, cancellationToken).ConfigureAwait(false);
        return new MenuListResponse(
            [.. summaries.Select(s => s.Summary)],
            Menu.MaxPerSite,
            await access.MenuPermissionsAsync(siteId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// One menu for the admin, its stored draft read at the current schema version (019
    /// FR-021); refused when the draft cannot be used, not found when there is no such menu.
    /// </summary>
    public async Task<Result<MenuDetailResponse>> ReadAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var found = (await SummariesAsync(siteId, menuId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (found is null)
        {
            return Result.Failure<MenuDetailResponse>(PagesErrors.MenuNotFound);
        }

        var draft = stored.ForEditing(SubjectKinds.Menu, menuId, found.Document, found.SchemaVersion);
        return draft.IsFailure
            ? Result.Failure<MenuDetailResponse>(draft.Error)
            : Result.Success(await DetailAsync(siteId, found, draft.Value, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>One menu with its draft and target states, or null when the site has no such menu.</summary>
    public async Task<MenuDetailResponse?> OneAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var found = (await SummariesAsync(siteId, menuId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (found is null)
        {
            return null;
        }

        using var draft = JsonDocument.Parse(found.Document);
        return await DetailAsync(siteId, found, draft.RootElement.Clone(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<MenuDetailResponse> DetailAsync(Guid siteId, Found found, JsonElement draft, CancellationToken cancellationToken)
    {
        var s = found.Summary;
        return new MenuDetailResponse(
            s.Id,
            s.Name,
            s.EntryCount,
            s.BrokenCount,
            s.UsedBy,
            s.Version,
            s.DraftVersion,
            s.CreatedAtUtc,
            s.UpdatedAtUtc,
            s.Publication,
            draft,
            found.Targets,
            await access.MenuPermissionsAsync(siteId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Whether the site has the menu.</summary>
    public Task<bool> ExistsAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken) =>
        database.ForSite<Menu>(siteId).AnyAsync(m => m.Id == menuId, cancellationToken);

    private sealed record Found(MenuResponse Summary, string Document, int SchemaVersion, IReadOnlyList<MenuTargetStateResponse> Targets);

    private async Task<List<Found>> SummariesAsync(Guid siteId, Guid? menuId, CancellationToken cancellationToken)
    {
        var menus = database.ForSite<Menu>(siteId).AsNoTracking();
        if (menuId is { } only)
        {
            menus = menus.Where(m => m.Id == only);
        }

        // xmin is read as it is and widened after: PostgreSQL has no cast from xid to bigint.
        var rows = await menus
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Version,
                m.CreatedAtUtc,
                m.UpdatedAtUtc,
                Draft = database.ForSite<MenuDraft>(siteId).Where(d => d.MenuId == m.Id).Select(d => new { d.Document, d.Version, d.SchemaVersion }).First(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();

        // FR-003: who shows each menu, in a draft or a live version.
        var draftUsers = await database.ForSite<MenuUsage>(siteId)
            .Where(u => ids.Contains(u.MenuId))
            .Select(u => new { u.MenuId, u.SubjectKind, u.SubjectId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var liveUsers = await database.ForSite<LiveDependency>(siteId)
            .Where(d => d.TargetKind == DependencyKinds.Menu && ids.Contains(d.TargetId))
            .Select(d => new { MenuId = d.TargetId, d.SubjectKind, d.SubjectId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var usedBy = draftUsers.Concat(liveUsers)
            .Distinct()
            .GroupBy(u => u.MenuId)
            .ToDictionary(g => g.Key, g => g.Count());

        var targetsByMenu = rows.ToDictionary(r => r.Id, r => MenuTargets.Extract(r.Draft.Document));
        var states = await targetStates
            .StatesAsync(siteId, targetsByMenu.Values.SelectMany(t => t).Select(t => (t.Kind, t.Id)), cancellationToken)
            .ConfigureAwait(false);

        var publication = await publications.SummariesAsync(siteId, SubjectKinds.Menu, ids, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows.Select(r =>
            {
                var targets = targetsByMenu[r.Id];
                var targetStatesOfMenu = targets
                    .Select(t => (t.Kind, t.Id))
                    .Distinct()
                    .Select(t => states[t])
                    .ToList();
                var broken = targets.Count(t => states[(t.Kind, t.Id)].State == MenuTargetStateNames.Deleted);

                return new Found(
                    new MenuResponse(
                        r.Id,
                        r.Name,
                        MenuTargets.CountEntries(r.Draft.Document),
                        broken,
                        usedBy.GetValueOrDefault(r.Id),
                        r.Version,
                        r.Draft.Version,
                        r.CreatedAtUtc,
                        r.UpdatedAtUtc,
                        publication[r.Id]),
                    r.Draft.Document,
                    r.Draft.SchemaVersion,
                    targetStatesOfMenu);
            }),
        ];
    }
}
