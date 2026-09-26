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
internal sealed class Page : ISiteOwned
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

    /// <summary>Records that this page was the subject of a change.</summary>
    internal void Touch(Guid actor, DateTimeOffset now)
    {
        Version++;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }
}
