using System.Text.Json;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;

namespace QuickBuilder.Rendering.Domain;

/// <summary>
/// Everything one render reads, gathered before it starts (012 data-model §5).
/// </summary>
/// <remarks>
/// Read after the page's cache key, never before (research PR-05): anything published after
/// the key was read gets a newer key, so a render that saw older content is stored where no
/// one will look for it again.
/// </remarks>
/// <param name="Title">The live version's title, the document title visitors see.</param>
/// <param name="Document">The live page document.</param>
/// <param name="Design">The live design document (or the defaults).</param>
/// <param name="Components">Live component documents by id; a component without one is absent.</param>
/// <param name="LinkTargets">Link targets by page id; a page that does not exist is absent.</param>
/// <param name="Media">Media files by id; a deleted file is absent.</param>
/// <param name="Language">The site's default language, for <c>lang</c>.</param>
/// <param name="BasePath">The installation's base path, prefixed to every address on the site.</param>
/// <param name="Data">
/// The published items, schemas and list results the page's bindings and lists read (016
/// research TB-09); empty for a page with neither.
/// </param>
/// <param name="Seo">The live document's search metadata, or null when it has none (017 research SE-02).</param>
/// <param name="Site">
/// The site, for its name, base URL and search settings (017 research SE-05); null for renders
/// that write no head metadata, such as the not-found page.
/// </param>
/// <param name="Path">The address the page answers, for its canonical address (017 FR-011).</param>
/// <param name="IsItemPage">Whether this is an item's page under a template (017 research SE-05).</param>
/// <param name="Menus">Live menus by id, with their published names, for Navigation blocks (024 NV-09).</param>
/// <param name="MenuItems">Published items that menu entries point at, by id (024 NV-09).</param>
/// <param name="ItemPatterns">Live template patterns by collection, for menu entries' item links.</param>
/// <param name="CurrentPageId">The page being rendered (for an item page, its template), for active state (024 NV-11).</param>
/// <param name="CurrentItemId">The item of an item page, for active state; null otherwise.</param>
internal sealed record RenderInputs(
    string Title,
    JsonElement Document,
    JsonElement Design,
    IReadOnlyDictionary<Guid, LiveDocument> Components,
    IReadOnlyDictionary<Guid, PageLinkTarget> LinkTargets,
    IReadOnlyDictionary<Guid, PublicMediaFile> Media,
    string Language,
    string BasePath,
    Binding.ResolvedData? Data = null,
    QuickBuilder.Rendering.Seo.SeoDocument? Seo = null,
    QuickBuilder.Sites.Contracts.SiteDto? Site = null,
    string Path = "/",
    bool IsItemPage = false,
    IReadOnlyDictionary<string, int>? PageParams = null,
    IReadOnlyList<QuickBuilder.Pages.Contracts.Publishing.LiveAncestor>? Ancestors = null,
    IReadOnlyDictionary<Guid, LiveMenu>? Menus = null,
    IReadOnlyDictionary<Guid, QuickBuilder.Cms.Contracts.Publishing.PublishedItem>? MenuItems = null,
    IReadOnlyDictionary<Guid, string>? ItemPatterns = null,
    Guid? CurrentPageId = null,
    Guid? CurrentItemId = null);
