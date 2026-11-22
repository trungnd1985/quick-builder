using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// Where <see cref="PageComposer"/> reads everything around a page document (022 research
/// PV-04): the live site for visitors (<see cref="LiveContentSource"/>), or the live site with
/// never-published parts filled from their drafts for a preview (PV-03).
/// </summary>
/// <remarks>
/// Bindings and lists are not here: they read published items through <c>BindingFetcher</c>
/// in both cases, because lists and references show published data only, also in a preview
/// (spec FR-029).
/// </remarks>
internal interface IContentSource
{
    /// <summary>The design the page is shown in.</summary>
    Task<PublishedDesign> GetDesignAsync(Guid siteId, CancellationToken cancellationToken);

    /// <summary>Component documents by id; a component with nothing to show is absent.</summary>
    Task<IReadOnlyDictionary<Guid, LiveDocument>> GetComponentsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken);

    /// <summary>Menu documents by id; a menu with nothing to show is absent.</summary>
    Task<IReadOnlyDictionary<Guid, LiveMenu>> GetMenusAsync(
        Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken cancellationToken);

    /// <summary>Published items menu entries point at.</summary>
    Task<IReadOnlyDictionary<Guid, PublishedItem>> GetItemsAsync(
        Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);

    /// <summary>Live template patterns by collection, for links to item pages.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTemplatePatternsAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken);

    /// <summary>Link targets by page id.</summary>
    Task<IReadOnlyDictionary<Guid, PageLinkTarget>> GetLinkTargetsAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken);

    /// <summary>Media files by id; a deleted file is absent.</summary>
    Task<IReadOnlyDictionary<Guid, PublicMediaFile>> GetMediaAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken);

    /// <summary>The live pages above a page, root first, for its breadcrumb trail.</summary>
    Task<IReadOnlyList<LiveAncestor>> GetAncestorsAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken);
}

/// <summary>What visitors get: live versions only, through the public read-only readers (012 PR-06).</summary>
internal sealed class LiveContentSource(
    IPublishedContentReader pages,
    IPublishedDesignReader designs,
    IPublicMediaReader media,
    IPublishedItemReader items) : IContentSource
{
    public Task<PublishedDesign> GetDesignAsync(Guid siteId, CancellationToken cancellationToken) =>
        designs.GetLiveAsync(siteId, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, LiveDocument>> GetComponentsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken) =>
        pages.GetLiveComponentsAsync(siteId, componentIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, LiveMenu>> GetMenusAsync(
        Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken cancellationToken) =>
        pages.GetLiveMenusAsync(siteId, menuIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, PublishedItem>> GetItemsAsync(
        Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        items.GetAsync(siteId, itemIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, string>> GetTemplatePatternsAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken) =>
        pages.GetLiveTemplatePatternsAsync(siteId, collectionIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, PageLinkTarget>> GetLinkTargetsAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken) =>
        pages.GetLinkTargetsAsync(siteId, pageIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, PublicMediaFile>> GetMediaAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken) =>
        media.GetAsync(siteId, mediaIds, cancellationToken);

    public Task<IReadOnlyList<LiveAncestor>> GetAncestorsAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken) =>
        pages.GetLiveAncestorsAsync(siteId, pageId, cancellationToken);
}
