using System.Text.Json;

namespace QuickBuilder.Pages.Features.Menus;

/// <summary>What the caller may do with the site's menus, so the admin offers only that.</summary>
public sealed record MenuPermissionsResponse(bool CanManage, bool CanPublish);

/// <summary>The state of one page or item a menu's draft points at (024 data-model §4.3).</summary>
/// <param name="Kind"><c>page</c> or <c>item</c>.</param>
/// <param name="State"><c>live</c>, <c>not_published</c>, <c>deleted</c> or <c>no_page</c> (an item without a live template).</param>
/// <param name="Title">The page's current title or the item's current name; null when deleted.</param>
public sealed record MenuTargetStateResponse(string Kind, Guid Id, string State, string? Title, bool InTrash = false);

/// <summary>One menu, as the menu list shows it (024 contracts/menus-api.yaml, <c>MenuSummary</c>).</summary>
/// <param name="EntryCount">Entries of the draft, at every level.</param>
/// <param name="BrokenCount">Entries of the draft whose page or item was deleted.</param>
/// <param name="UsedBy">Distinct pages and components that show the menu in their draft or live version.</param>
/// <param name="Version">The menu's version, for rename and delete.</param>
/// <param name="DraftVersion">The draft's version, for saving and publishing.</param>
public sealed record MenuResponse(
    Guid Id,
    string Name,
    int EntryCount,
    int BrokenCount,
    int UsedBy,
    uint Version,
    uint DraftVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PublicationResponse Publication);

/// <summary>One menu with its draft and the state of every target (contracts/menus-api.yaml, <c>Menu</c>).</summary>
public sealed record MenuDetailResponse(
    Guid Id,
    string Name,
    int EntryCount,
    int BrokenCount,
    int UsedBy,
    uint Version,
    uint DraftVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PublicationResponse Publication,
    JsonElement Draft,
    IReadOnlyList<MenuTargetStateResponse> Targets,
    MenuPermissionsResponse Permissions);

/// <summary>A site's menus, by name.</summary>
/// <param name="Limit">The most menus a site may hold.</param>
public sealed record MenuListResponse(IReadOnlyList<MenuResponse> Items, int Limit, MenuPermissionsResponse Permissions);

/// <summary>A menu's name, for create (with an optional menu to copy) and rename.</summary>
/// <param name="DuplicateOf">On create: the menu whose draft the new one starts from.</param>
/// <param name="Version">On rename: the menu version the member saw.</param>
public sealed record MenuNameRequest(string Name, Guid? DuplicateOf = null, uint? Version = null);

/// <summary>A menu draft to save, and the draft version the member saw.</summary>
public sealed record SaveMenuDraftRequest(JsonElement Document, uint Version);
