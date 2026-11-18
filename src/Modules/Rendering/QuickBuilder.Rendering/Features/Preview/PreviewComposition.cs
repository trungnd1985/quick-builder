using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// What a preview is composed of (022 research PV-03, spec clarification Q2): each part's live
/// version where it has one, its draft only when it was never published, and the name of every
/// part shown from a draft, for the preview bar (FR-020).
/// </summary>
/// <remarks>
/// Pure: the readers fetch, this decides. Lists, references and link targets are not here;
/// they show published data only, as on the public site (FR-029).
/// </remarks>
internal static class PreviewComposition
{
    /// <summary>Live components, with never-published ones filled from their drafts.</summary>
    public static Composed<LiveDocument> Components(
        IReadOnlyDictionary<Guid, LiveDocument> live, IReadOnlyDictionary<Guid, PreviewDraftDocument> unpublished)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(unpublished);

        var documents = new Dictionary<Guid, LiveDocument>(live);
        var parts = new List<PreviewDraftPart>();
        foreach (var (id, draft) in unpublished.OrderBy(d => d.Value.Name, StringComparer.Ordinal))
        {
            if (documents.TryAdd(id, new LiveDocument(draft.SchemaVersion, draft.Document)))
            {
                parts.Add(new PreviewDraftPart(PreviewPartKind.Component, draft.Name));
            }
        }

        return new Composed<LiveDocument>(documents, parts);
    }

    /// <summary>Live menus, with never-published ones filled from their drafts.</summary>
    public static Composed<LiveMenu> Menus(
        IReadOnlyDictionary<Guid, LiveMenu> live, IReadOnlyDictionary<Guid, PreviewDraftDocument> unpublished)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(unpublished);

        var documents = new Dictionary<Guid, LiveMenu>(live);
        var parts = new List<PreviewDraftPart>();
        foreach (var (id, draft) in unpublished.OrderBy(d => d.Value.Name, StringComparer.Ordinal))
        {
            if (documents.TryAdd(id, new LiveMenu(draft.SchemaVersion, draft.Document, draft.Name)))
            {
                parts.Add(new PreviewDraftPart(PreviewPartKind.Menu, draft.Name));
            }
        }

        return new Composed<LiveMenu>(documents, parts);
    }

    /// <summary>
    /// The live design; the saved tokens instead while no design was ever published (live
    /// number 0) and a token set was saved.
    /// </summary>
    public static (PublishedDesign Design, PreviewDraftPart? FromDraft) Design(PublishedDesign live, DesignTokenSetDto saved)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(saved);

        return live.Number == 0 && !saved.IsDefault
            ? (live with { Document = saved.Document }, new PreviewDraftPart(PreviewPartKind.Design, string.Empty))
            : (live, null);
    }

    /// <summary>
    /// The template an item preview renders through: its live version, or its draft when it
    /// was never published; null when the collection has no template page.
    /// </summary>
    public static (LivePageVersion Version, Guid PageId, PreviewDraftPart? FromDraft)? Template(PreviewPageVersion? live, PreviewPage? draft)
    {
        if (live is not null)
        {
            return (new LivePageVersion(live.SchemaVersion, live.Document, live.Title), live.PageId, null);
        }

        return draft is null
            ? null
            : (new LivePageVersion(draft.SchemaVersion, draft.Document, draft.Title), draft.PageId, new PreviewDraftPart(PreviewPartKind.Template, draft.Title));
    }

    /// <summary>
    /// The item a template page preview shows: its published values, or its draft values when
    /// it has none; null when the item is not readable.
    /// </summary>
    public static (PublishedItem Item, PreviewDraftPart? FromDraft)? SampleItem(PublishedItem? published, PreviewItem? draft)
    {
        if (published is not null)
        {
            return (published, null);
        }

        return draft is null
            ? null
            : (draft.Item, new PreviewDraftPart(PreviewPartKind.SampleItem, draft.Item.Name ?? draft.Item.Slug));
    }
}

/// <summary>Documents by id, and the parts among them shown from their drafts.</summary>
internal sealed record Composed<T>(IReadOnlyDictionary<Guid, T> Documents, IReadOnlyList<PreviewDraftPart> FromDrafts);
