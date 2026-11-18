using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Rendering;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// What a preview reads around its page (022 research PV-03): the live site, with
/// never-published components, menus and design filled from their drafts and recorded for the
/// bar, and media addressed through the preview (PV-06).
/// </summary>
/// <remarks>
/// One per render: it remembers the parts it showed from drafts. Lists, items menu entries
/// point at, link targets and ancestors are the live ones, as visitors see them (spec FR-029).
/// </remarks>
internal sealed class PreviewContentSource(
    LiveContentSource live,
    IPreviewContentReader drafts,
    IDesignTokenReader designTokens,
    PreviewAddresses addresses) : IContentSource
{
    private readonly List<PreviewDraftPart> _fromDrafts = [];

    /// <summary>The parts shown from their drafts so far, in the order they were read.</summary>
    public IReadOnlyList<PreviewDraftPart> FromDrafts => _fromDrafts;

    public async Task<PublishedDesign> GetDesignAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var published = await live.GetDesignAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (published.Number != 0)
        {
            return published;
        }

        var saved = await designTokens.ReadAsync(siteId, cancellationToken).ConfigureAwait(false);
        var (design, fromDraft) = PreviewComposition.Design(published, saved);
        Record(fromDraft);
        return design;
    }

    public async Task<IReadOnlyDictionary<Guid, LiveDocument>> GetComponentsAsync(
        Guid siteId, IReadOnlyCollection<Guid> componentIds, CancellationToken cancellationToken)
    {
        var published = await live.GetComponentsAsync(siteId, componentIds, cancellationToken).ConfigureAwait(false);
        var missing = componentIds.Where(id => !published.ContainsKey(id)).ToList();
        if (missing.Count == 0)
        {
            return published;
        }

        var unpublished = await drafts.GetUnpublishedComponentDraftsAsync(siteId, missing, cancellationToken).ConfigureAwait(false);
        var composed = PreviewComposition.Components(published, unpublished);
        _fromDrafts.AddRange(composed.FromDrafts);
        return composed.Documents;
    }

    public async Task<IReadOnlyDictionary<Guid, LiveMenu>> GetMenusAsync(
        Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken cancellationToken)
    {
        var published = await live.GetMenusAsync(siteId, menuIds, cancellationToken).ConfigureAwait(false);
        var missing = menuIds.Where(id => !published.ContainsKey(id)).ToList();
        if (missing.Count == 0)
        {
            return published;
        }

        var unpublished = await drafts.GetUnpublishedMenuDraftsAsync(siteId, missing, cancellationToken).ConfigureAwait(false);
        var composed = PreviewComposition.Menus(published, unpublished);
        _fromDrafts.AddRange(composed.FromDrafts);
        return composed.Documents;
    }

    public Task<IReadOnlyDictionary<Guid, PublishedItem>> GetItemsAsync(
        Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        live.GetItemsAsync(siteId, itemIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, string>> GetTemplatePatternsAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken) =>
        live.GetTemplatePatternsAsync(siteId, collectionIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, PageLinkTarget>> GetLinkTargetsAsync(
        Guid siteId, IReadOnlyCollection<Guid> pageIds, CancellationToken cancellationToken) =>
        live.GetLinkTargetsAsync(siteId, pageIds, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, PublicMediaFile>> GetMediaAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken)
    {
        var files = await live.GetMediaAsync(siteId, mediaIds, cancellationToken).ConfigureAwait(false);
        return files.ToDictionary(f => f.Key, f => addresses.Media(f.Value));
    }

    public Task<IReadOnlyList<LiveAncestor>> GetAncestorsAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken) =>
        live.GetAncestorsAsync(siteId, pageId, cancellationToken);

    /// <summary>Records a part the caller itself chose to show from its draft (a template, a sample item).</summary>
    public void Record(PreviewDraftPart? part)
    {
        if (part is not null)
        {
            _fromDrafts.Add(part);
        }
    }
}
