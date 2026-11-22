using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering.Navigation;
using QuickBuilder.Rendering.Seo;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// Gathers everything one render reads around a page document (012 data-model §5; 016
/// research TB-09; 017 SE-05, SE-18; 024 NV-09), from a <see cref="IContentSource"/>.
/// </summary>
/// <remarks>
/// The one place inputs are gathered (022 research PV-04): published pages read the live
/// source, previews a source that fills never-published parts from their drafts. Both then
/// render through <see cref="PageResponder.Render"/>, so a preview is what publishing the same
/// content would serve (spec FR-002).
/// </remarks>
internal sealed class PageComposer(BindingFetcher bindings, ILogger<PageComposer> logger)
{
    /// <summary>The inputs for <paramref name="version"/>, shown as page <paramref name="pageId"/>.</summary>
    /// <param name="pageItem">The item of an item page, else null.</param>
    public async Task<RenderInputs> ComposeAsync(
        IContentSource source,
        SiteDto site,
        Guid pageId,
        LivePageVersion version,
        string basePath,
        PublishedItem? pageItem,
        ListRequest request,
        CancellationToken cancellationToken,
        FormState? formState = null,
        PreviewContext? preview = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(request);

        var design = await source.GetDesignAsync(site.Id, cancellationToken).ConfigureAwait(false);

        var references = new RenderReferences();
        references.Collect(version.Document);

        var components = await source.GetComponentsAsync(site.Id, references.Components, cancellationToken).ConfigureAwait(false);
        foreach (var component in components.Values)
        {
            references.Collect(component.Document);
        }

        // 016 TB-09: what the bindings read, and what their values show and link to.
        var fetched = await bindings.FetchAsync(
                site.Id, [version.Document, .. components.Values.Select(c => c.Document)], pageItem, request.Params.Pages, request.Path, cancellationToken)
            .ConfigureAwait(false);
        if (fetched.SkippedLists is { Count: > 0 } skipped)
        {
            // TB-09: ids only, never values (FR-038).
            logger.LogWarning(
                "Page {PageId} of site {SiteId} needed more than {Budget} list queries; lists {ListIds} show their empty state.",
                pageId,
                site.Id,
                ListQueries.MaxQueriesPerRender,
                string.Join(", ", skipped.Distinct(StringComparer.Ordinal)));
        }

        references.Pages.UnionWith(fetched.Pages);
        references.Media.UnionWith(fetched.Media);

        // 017 research SE-05: the sharing image, the site's default one, fetched with the rest.
        var seo = SeoDocument.From(version.Document);
        references.Media.UnionWith(HeadBuilder.MediaIds(seo, site));

        // 017 FR-012: a canonical override's target, to name its current address.
        if (seo?.Canonical?.PageId is { } canonicalPage)
        {
            references.Pages.Add(canonicalPage);
        }

        // 024 NV-09: the menus Navigation blocks show, and what their entries point at, batched
        // with the page's other links. Nothing is read for a page without a menu.
        var menus = await source.GetMenusAsync(site.Id, references.Menus, cancellationToken).ConfigureAwait(false);
        var menuItemIds = new HashSet<Guid>();
        foreach (var menu in menus.Values)
        {
            NavigationReferences.Collect(menu.Document, references.Pages, menuItemIds);
        }

        var menuItems = menuItemIds.Count == 0
            ? new Dictionary<Guid, PublishedItem>()
            : await source.GetItemsAsync(site.Id, menuItemIds, cancellationToken).ConfigureAwait(false);
        var itemPatterns = menuItems.Count == 0
            ? new Dictionary<Guid, string>()
            : await source.GetTemplatePatternsAsync(site.Id, [.. menuItems.Values.Select(i => i.CollectionId).Distinct()], cancellationToken).ConfigureAwait(false);

        var targets = await source.GetLinkTargetsAsync(site.Id, references.Pages, cancellationToken).ConfigureAwait(false);
        var files = await source.GetMediaAsync(site.Id, references.Media, cancellationToken).ConfigureAwait(false);

        // 017 research SE-18: the breadcrumb trail, read on a miss only; the generation moves
        // when an ancestor's live title does (T100).
        var ancestors = request.Path == "/" && pageItem is null
            ? []
            : await source.GetAncestorsAsync(site.Id, pageId, cancellationToken).ConfigureAwait(false);

        // 016 FR-030: an item page is titled by the item's name, else by the template's title.
        var title = string.IsNullOrWhiteSpace(pageItem?.Name) ? version.Title : pageItem.Name;

        return new RenderInputs(
            title, version.Document, design.Document, components, targets, files, site.DefaultLanguage, basePath, fetched.Data,
            seo, site, request.Path, pageItem is not null, request.Params.Pages, ancestors,
            menus, menuItems, itemPatterns, pageId, pageItem?.Id, formState, preview);
    }
}
