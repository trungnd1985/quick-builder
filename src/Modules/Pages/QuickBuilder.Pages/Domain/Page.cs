using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// One page's place in its site's page tree (data-model.md, <c>pages.pages</c>).
/// </summary>
/// <remarks>
/// Not an aggregate root: a page belongs to its site's <see cref="PageTree"/>, because every
/// rule about where a page may be — unique addresses, no cycles, depth, one home page — is
/// a rule about the whole tree (research P-02). Its members that change it are
/// <c>internal</c> only because C# has no narrower way to say "the tree may call this";
/// nothing but <see cref="PageTree"/> does, and it validates before it calls.
/// <para>
/// The draft content lives in <see cref="PageDraft"/>, a separate aggregate, so loading a
/// 2,000-page tree never loads 2,000 documents (research P-08).
/// </para>
/// </remarks>
internal sealed partial class Page : ISiteOwned, ITrashable
{
    public const int MaxTitleLength = 200;

    private Page(
        Guid id,
        Guid siteId,
        Guid? parentId,
        string title,
        string slug,
        bool isHome,
        Guid actor,
        DateTimeOffset now)
    {
        Id = id;
        SiteId = siteId;
        ParentId = parentId;
        Title = title;
        Slug = slug;
        Path = string.Empty;
        IsHome = isHome;
        Version = 1;
        CreatedAtUtc = now;
        CreatedBy = actor;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    // EF Core materialisation.
    private Page()
    {
        Title = string.Empty;
        Slug = string.Empty;
        Path = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>Null for a top-level page.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>0-based and contiguous among the pages with the same parent (research P-06).</summary>
    public int Position { get; private set; }

    public string Title { get; private set; }

    /// <summary>Kept while the page is the home page, where it appears in no address (P-07).</summary>
    public string Slug { get; private set; }

    /// <summary>The page's address, computed by the tree and never typed (FR-004).</summary>
    public string Path { get; private set; }

    /// <summary>1 for a top-level page.</summary>
    public int Depth { get; private set; }

    public bool IsHome { get; private set; }

    /// <summary>
    /// The collection this page is the template of, or null for an ordinary page (016 research
    /// TB-02). A template's <see cref="Path"/> is its address pattern, its parent's address
    /// followed by <c>/{slug}</c>; its own <see cref="Slug"/> is kept, unused, as the home
    /// page keeps its own, so it has an address again if it stops being a template.
    /// </summary>
    public Guid? TemplateCollectionId { get; private set; }

    public bool IsTemplate => TemplateCollectionId is not null;

    /// <summary>023 FR-021: the page this one is a copy of; null unless made by "save my version as a copy".</summary>
    public Guid? CopiedFromId { get; private set; }

    /// <summary>When the source version the member had loaded was saved.</summary>
    public DateTimeOffset? CopiedFromSavedAtUtc { get; private set; }

    public Guid? CopiedBy { get; private set; }

    public DateTimeOffset? CopiedAtUtc { get; private set; }

    /// <summary>The copy origin, when this is a copy.</summary>
    public CopyOrigin? CopyOrigin =>
        CopiedFromId is { } source && CopiedFromSavedAtUtc is { } saved && CopiedBy is { } by && CopiedAtUtc is { } at
            ? new CopyOrigin(source, saved, by, at)
            : null;

    /// <summary>Records that this page was made as a copy (023 FR-020, FR-021).</summary>
    public void MarkCopyOf(CopyOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        CopiedFromId = origin.SourceId;
        CopiedFromSavedAtUtc = origin.SourceSavedAtUtc;
        CopiedBy = origin.CopiedBy;
        CopiedAtUtc = origin.CopiedAtUtc;
    }

    /// <summary>
    /// Moves when <em>this</em> page is changed: its title, slug, parent, place or home flag
    /// (research P-05). A change to another page — renumbering the siblings around a move,
    /// or an ancestor's new slug changing this page's address — does not move it, so it
    /// never makes a user's pending change to this page out of date (FR-010).
    /// </summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>When the page was moved to the trash; null while it is live (021 TR-02).</summary>
    public DateTimeOffset? TrashedAtUtc { get; private set; }

    public Guid? TrashedBy { get; private set; }

    /// <summary>The parent it had when trashed; null when it was top level. No foreign key (TR-05).</summary>
    public Guid? TrashedParentId { get; private set; }

    public int? TrashedPosition { get; private set; }

    /// <summary>Its address when trashed, shown and searched in the trash (TR-19).</summary>
    public string? TrashedPath { get; private set; }

    public bool? TrashedWasHome { get; private set; }

    /// <summary>The collection it was the template of (TR-12).</summary>
    public Guid? TrashedTemplateCollectionId { get; private set; }

    /// <summary>Whether a publish was scheduled when it was trashed (TR-07).</summary>
    public bool? TrashedHadSchedule { get; private set; }

    public bool IsTrashed => TrashedAtUtc is not null;

    /// <summary>The trimmed title, or null when it is empty or too long.</summary>
    public static string? NormaliseTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxTitleLength ? null : trimmed;
    }

    internal static Page New(Guid siteId, Guid? parentId, string title, string slug, bool isHome, Guid actor, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), siteId, parentId, title, slug, isHome, actor, now);

    internal void Rename(string title, string slug)
    {
        Title = title;
        Slug = slug;
    }

    internal void Reparent(Guid? parentId) => ParentId = parentId;

    internal void PlaceAt(int position) => Position = position;

    internal void SetAddress(string path, int depth)
    {
        Path = path;
        Depth = depth;
    }

    internal void SetHome(bool isHome) => IsHome = isHome;

    internal void SetTemplate(Guid? collectionId) => TemplateCollectionId = collectionId;

    /// <summary>
    /// Marks the page as in the trash and detaches it from the tree's foreign keys and
    /// checks, keeping where it was in plain columns (021 TR-05). Only the tree calls this.
    /// </summary>
    internal void MoveToTrash(Guid actor, bool hadSchedule, DateTimeOffset now)
    {
        TrashedAtUtc = now;
        TrashedBy = actor;
        TrashedParentId = ParentId;
        TrashedPosition = Position;
        TrashedPath = Path;
        TrashedWasHome = IsHome;
        TrashedTemplateCollectionId = TemplateCollectionId;
        TrashedHadSchedule = hadSchedule;

        ParentId = null;
        IsHome = false;
        TemplateCollectionId = null;
        Touch(actor, now);
    }

    /// <summary>Records whether a publish was scheduled, once publishing has been shelved (021 TR-07).</summary>
    internal void RecordTrashedSchedule(bool hadSchedule) => TrashedHadSchedule = hadSchedule;

    /// <summary>Clears every trash column; the tree places the page and computes its address.</summary>
    internal void TakeOutOfTrash(Guid actor, DateTimeOffset now)
    {
        TrashedAtUtc = null;
        TrashedBy = null;
        TrashedParentId = null;
        TrashedPosition = null;
        TrashedPath = null;
        TrashedWasHome = null;
        TrashedTemplateCollectionId = null;
        TrashedHadSchedule = null;
        Touch(actor, now);
    }

    /// <summary>Records that this page was the subject of a change.</summary>
    internal void Touch(Guid actor, DateTimeOffset now)
    {
        Version++;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }
}
