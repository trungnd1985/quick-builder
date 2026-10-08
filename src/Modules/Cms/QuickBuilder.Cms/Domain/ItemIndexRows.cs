using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// One slug an item holds, draft or published (015 research IT-03). The table's key on
/// <c>(collection_id, slug)</c> is what makes slugs unique across both, even under concurrent
/// saves. Rebuilt by <c>ItemIndexer</c> on every write.
/// </summary>
internal sealed class ItemSlug : ISiteOwned
{
    public ItemSlug(Guid siteId, Guid collectionId, string slug, Guid itemId)
    {
        SiteId = siteId;
        CollectionId = collectionId;
        Slug = slug;
        ItemId = itemId;
    }

    // EF Core materialisation.
    private ItemSlug()
    {
        Slug = string.Empty;
    }

    public Guid CollectionId { get; private set; }

    public string Slug { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid SiteId { get; private set; }
}

/// <summary>
/// One item referring to another through a field, and in which copies (015 research IT-10). The
/// target's foreign key (<c>ON DELETE NO ACTION</c>) refuses deleting an item something refers
/// to. Rebuilt by <c>ItemIndexer</c> on every write.
/// </summary>
internal sealed class ItemReference : ISiteOwned
{
    public ItemReference(Guid siteId, Guid sourceItemId, string fieldId, Guid targetItemId, bool inDraft, bool inPublished, bool inScheduled)
    {
        SiteId = siteId;
        SourceItemId = sourceItemId;
        FieldId = fieldId;
        TargetItemId = targetItemId;
        InDraft = inDraft;
        InPublished = inPublished;
        InScheduled = inScheduled;
    }

    // EF Core materialisation.
    private ItemReference()
    {
        FieldId = string.Empty;
    }

    public Guid SiteId { get; private set; }

    public Guid SourceItemId { get; private set; }

    public string FieldId { get; private set; }

    public Guid TargetItemId { get; private set; }

    public bool InDraft { get; private set; }

    public bool InPublished { get; private set; }

    public bool InScheduled { get; private set; }
}

/// <summary>
/// One media file an item uses, and in which copies (015 research IT-15), for the media
/// library's usage and its delete rules. Rebuilt by <c>ItemIndexer</c> on every write.
/// </summary>
internal sealed class ItemMediaUse : ISiteOwned
{
    public ItemMediaUse(Guid siteId, Guid itemId, Guid mediaId, bool inDraft, bool inLive, bool inScheduled, bool inHistory)
    {
        SiteId = siteId;
        ItemId = itemId;
        MediaId = mediaId;
        InDraft = inDraft;
        InLive = inLive;
        InScheduled = inScheduled;
        InHistory = inHistory;
    }

    public Guid SiteId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid MediaId { get; private set; }

    public bool InDraft { get; private set; }

    public bool InLive { get; private set; }

    public bool InScheduled { get; private set; }

    /// <summary>Kept versions other than the live one show the file.</summary>
    public bool InHistory { get; private set; }
}
