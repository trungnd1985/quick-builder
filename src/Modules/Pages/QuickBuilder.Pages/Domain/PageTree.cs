using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Domain.Events;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Domain;

/// <summary>
/// The page structure of one site: the aggregate that owns every structural rule
/// (research P-02, data-model.md "Invariants of PageTree").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the whole tree.</b> Whether a move is allowed depends on every other page: the
/// moved subtree's new addresses must collide with nobody, and the target must not be one
/// of the page's own descendants. Two moves that are each fine alone (A under B, B under A)
/// together make a cycle no index can refuse. So every structural change loads the tree,
/// asks it, and saves it; the root row's <see cref="Version"/> (<c>xmin</c>) makes
/// concurrent changes to one site serialise, and the loser retries against the fresh tree
/// (P-05). At the 2,000-page limit the structural rows are a few hundred kilobytes.
/// </para>
/// <para>
/// <b>How a change is made.</b> Every method validates first and mutates last. Anything
/// that can change an address goes through <see cref="Plan"/>, which recomputes every
/// page's address and depth from a proposed shape of the tree and refuses the whole change
/// on the first broken rule. Only then is anything applied, so a refused change leaves the
/// tree — and its recorded events — exactly as they were.
/// </para>
/// <para>
/// <b>Addresses</b> (FR-004, P-03): the home page is <c>/</c>; a top-level page and a child
/// of the home page are <c>/slug</c>; every other page is its parent's address, <c>/</c>,
/// then its slug. Slugs are lowercase, so addresses are too, and "ignoring letter case"
/// (FR-006) holds by construction.
/// </para>
/// </remarks>
internal sealed partial class PageTree : AggregateRoot, ISiteOwned
{
    private readonly List<Page> _pages = [];

    private PageTree(Guid siteId, DateTimeOffset now)
    {
        SiteId = siteId;
        UpdatedAtUtc = now;
    }

    // EF Core materialisation.
    private PageTree()
    {
    }

    public Guid SiteId { get; private set; }

    /// <summary>How many pages the site has. Equal to <see cref="Pages"/>' count once loaded.</summary>
    public int PageCount { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>The concurrency token, mapped to <c>xmin</c>. Never sent to clients.</summary>
    public uint Version { get; private set; }

    /// <summary>
    /// The live pages. A trashed page EF fixes up into the navigation (a restore loads it while
    /// the tree is tracked) is not one of them until it is restored (021 TR-10).
    /// </summary>
    public IReadOnlyList<Page> Pages => Live;

    private List<Page> Live => _pages.FindAll(p => !p.IsTrashed);

    /// <summary>A site's tree before its first page.</summary>
    public static PageTree Start(Guid siteId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new PageTree(siteId, clock.GetUtcNow());
    }

    /// <summary>Adds a page (FR-002, FR-003). Its draft is created by the caller (FR-015).</summary>
    public Result<Page> CreatePage(
        string? title,
        string? slug,
        Guid? parentId,
        Placement placement,
        bool isHome,
        Guid actor,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Page.NormaliseTitle(title) is not { } cleanTitle)
        {
            return Result.Failure<Page>(PagesErrors.InvalidTitle);
        }

        if (!PageAddresses.IsValidSlug(slug))
        {
            return Result.Failure<Page>(PagesErrors.InvalidSlug);
        }

        if (Live.Count >= PageAddresses.MaxPagesPerSite)
        {
            return Result.Failure<Page>(PagesErrors.TooManyPages);
        }

        if (parentId is { } parentKey && Find(parentKey) is null)
        {
            return Result.Failure<Page>(PagesErrors.ParentNotFound);
        }

        // 016 TB-02: a template's address ends in its item's slug, so nothing sits under it.
        if (parentId is { } templateKey && Find(templateKey)!.IsTemplate)
        {
            return Result.Failure<Page>(PagesErrors.TemplateHasChildren);
        }

        if (isHome && parentId is not null)
        {
            return Result.Failure<Page>(PagesErrors.HomePageMustBeTopLevel);
        }

        if (isHome && Home is not null)
        {
            return Result.Failure<Page>(PagesErrors.HomePageExists);
        }

        var siblings = Children(parentId);
        var index = ResolvePlacement(siblings, parentId, placement, moving: null);
        if (index.IsFailure)
        {
            return Result.Failure<Page>(index.Error);
        }

        var now = clock.GetUtcNow();
        var page = Page.New(SiteId, parentId, cleanTitle, slug!, isHome, actor, now);

        var plan = Plan(Shape.Of, added: new Shape(page.Id, parentId, page.Slug, isHome, index.Value));
        if (plan.IsFailure)
        {
            return Result.Failure<Page>(plan.Error);
        }

        _pages.Add(page);
        siblings.Insert(index.Value, page);
        Renumber(siblings);
        Apply(plan.Value);
        PageCount = Live.Count;
        UpdatedAtUtc = now;

        Raise(new PageCreatedEvent(SiteId, page.Id, parentId, page.Path, now));
        if (isHome)
        {
            Raise(new HomePageChangedEvent(SiteId, page.Id, PreviousPageId: null, now));
        }

        return Result.Success(page);
    }

    /// <summary>
    /// Changes a page's title and slug (FR-008). A new slug recomputes the page's address and
    /// every descendant's in the same change, or refuses it whole.
    /// </summary>
    public Result Rename(Guid pageId, string? title, string? slug, int version, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Find(pageId) is not { } page)
        {
            return Result.Failure(PagesErrors.NotFound);
        }

        if (page.Version != version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        if (Page.NormaliseTitle(title) is not { } cleanTitle)
        {
            return Result.Failure(PagesErrors.InvalidTitle);
        }

        if (!PageAddresses.IsValidSlug(slug))
        {
            return Result.Failure(PagesErrors.InvalidSlug);
        }

        if (cleanTitle == page.Title && slug == page.Slug)
        {
            return Result.Success();
        }

        var plan = Plan(p => p.Id == pageId ? Shape.Of(p) with { Slug = slug! } : Shape.Of(p));
        if (plan.IsFailure)
        {
            return Result.Failure(plan.Error);
        }

        var now = clock.GetUtcNow();
        page.Rename(cleanTitle, slug!);
        page.Touch(actor, now);
        var changes = Apply(plan.Value);
        UpdatedAtUtc = now;
        RaiseAddressChanges(changes, now);
        return Result.Success();
    }

    /// <summary>
    /// Moves a page, with its whole subtree, under <paramref name="parentId"/> (null: the top
    /// level), placed after <paramref name="afterPageId"/> (null: first) — FR-009. Reordering
    /// siblings is a move within the same parent.
    /// </summary>
    /// <remarks>
    /// The caller names the sibling to follow rather than an index, so a concurrent change
    /// to other siblings does not send the page somewhere the caller did not mean. If that
    /// sibling is no longer under the target parent, the caller's view is out of date
    /// (FR-010, research P-05).
    /// </remarks>
    public Result Move(Guid pageId, Guid? parentId, Guid? afterPageId, int version, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Find(pageId) is not { } page)
        {
            return Result.Failure(PagesErrors.NotFound);
        }

        if (page.Version != version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        if (parentId is { } parentKey)
        {
            if (Find(parentKey) is null)
            {
                return Result.Failure(PagesErrors.ParentNotFound);
            }

            if (parentKey == pageId || IsDescendant(parentKey, of: pageId))
            {
                return Result.Failure(PagesErrors.MoveIntoDescendant);
            }

            if (page.IsHome)
            {
                return Result.Failure(PagesErrors.HomePageMustBeTopLevel);
            }

            // 016 TB-02: nothing sits under a template.
            if (Find(parentKey)!.IsTemplate)
            {
                return Result.Failure(PagesErrors.TemplateHasChildren);
            }
        }

        if (page.IsTemplate)
        {
            // A template keeps a fixed segment before its items' slugs (TB-02), and its parent
            // holds one template at most.
            if (parentId is not { } newParent || Find(newParent)!.IsHome)
            {
                return Result.Failure(PagesErrors.TemplateNeedsParent);
            }

            if (Live.Any(p => p.IsTemplate && p.ParentId == parentId && p.Id != pageId))
            {
                return Result.Failure(PagesErrors.TemplateParentTaken);
            }
        }

        var sameParent = page.ParentId == parentId;

        // "After myself" under my own parent means "where I already am".
        if (sameParent && afterPageId == pageId)
        {
            return Result.Success();
        }

        var targetSiblings = Children(parentId);
        targetSiblings.Remove(page);

        var placement = afterPageId is { } anchor ? Placement.After(anchor) : Placement.First;
        var index = ResolvePlacement(targetSiblings, parentId, placement, moving: page);
        if (index.IsFailure)
        {
            return Result.Failure(index.Error);
        }

        if (sameParent && index.Value == page.Position)
        {
            return Result.Success();
        }

        IReadOnlyList<Planned> plan = [];
        if (!sameParent)
        {
            var planned = Plan(p => p.Id == pageId ? Shape.Of(p) with { ParentId = parentId } : Shape.Of(p));
            if (planned.IsFailure)
            {
                return Result.Failure(planned.Error);
            }

            plan = planned.Value;
        }

        var now = clock.GetUtcNow();

        if (!sameParent)
        {
            var formerSiblings = Children(page.ParentId);
            formerSiblings.Remove(page);
            Renumber(formerSiblings);
            page.Reparent(parentId);
        }

        targetSiblings.Insert(index.Value, page);
        Renumber(targetSiblings);
        page.Touch(actor, now);
        var changes = Apply(plan);
        UpdatedAtUtc = now;
        RaiseAddressChanges(changes, now);
        return Result.Success();
    }

    /// <summary>
    /// Makes a top-level page the home page (FR-003, research P-07). The former home page
    /// becomes an ordinary top-level page at <c>/its-slug</c>, and both subtrees' addresses
    /// are recomputed together.
    /// </summary>
    public Result MakeHome(Guid pageId, int version, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Find(pageId) is not { } page)
        {
            return Result.Failure(PagesErrors.NotFound);
        }

        if (page.Version != version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        if (page.IsHome)
        {
            return Result.Success();
        }

        if (page.ParentId is not null)
        {
            return Result.Failure(PagesErrors.HomePageMustBeTopLevel);
        }

        var former = Home;

        // 016 TB-02: the home page's children read as top-level pages, so a template under the
        // new home page would lose its fixed segment.
        if (Children(pageId).Any(c => c.IsTemplate))
        {
            return Result.Failure(PagesErrors.TemplateNeedsParent);
        }
        var plan = Plan(p =>
            p.Id == pageId ? Shape.Of(p) with { IsHome = true }
            : p == former ? Shape.Of(p) with { IsHome = false }
            : Shape.Of(p));
        if (plan.IsFailure)
        {
            return Result.Failure(plan.Error);
        }

        var now = clock.GetUtcNow();
        if (former is not null)
        {
            former.SetHome(false);
            former.Touch(actor, now);
        }

        page.SetHome(true);
        page.Touch(actor, now);
        var changes = Apply(plan.Value);
        UpdatedAtUtc = now;

        Raise(new HomePageChangedEvent(SiteId, page.Id, former?.Id, now));
        RaiseAddressChanges(changes, now);
        return Result.Success();
    }

    /// <summary>
    /// Makes a page the template of <paramref name="collectionId"/>, changes its collection, or,
    /// with null, turns it back into an ordinary page (016 FR-002, FR-003, FR-009; research TB-02).
    /// </summary>
    /// <remarks>
    /// A template's address is its parent's address followed by <c>/{slug}</c>; turning it back
    /// gives it its own slug's address again, which must be free. Whether the collection is one
    /// of the site's is the caller's to check first: it belongs to another module.
    /// </remarks>
    public Result SetTemplate(Guid pageId, Guid? collectionId, int version, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Find(pageId) is not { } page)
        {
            return Result.Failure(PagesErrors.NotFound);
        }

        if (page.Version != version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        if (page.TemplateCollectionId == collectionId)
        {
            return Result.Success();
        }

        if (collectionId is { } collection)
        {
            if (page.IsHome || page.ParentId is not { } parentId || Find(parentId)!.IsHome)
            {
                return Result.Failure(PagesErrors.TemplateNeedsParent);
            }

            if (Live.Any(p => p.ParentId == pageId))
            {
                return Result.Failure(PagesErrors.TemplateHasChildren);
            }

            if (Live.Find(p => p.TemplateCollectionId == collection && p.Id != pageId) is { } other)
            {
                return Result.Failure(PagesErrors.CollectionHasTemplate(other.Id));
            }

            if (Live.Any(p => p.IsTemplate && p.ParentId == parentId && p.Id != pageId))
            {
                return Result.Failure(PagesErrors.TemplateParentTaken);
            }
        }

        var plan = Plan(p => p.Id == pageId ? Shape.Of(p) with { IsTemplate = collectionId is not null } : Shape.Of(p));
        if (plan.IsFailure)
        {
            return Result.Failure(plan.Error);
        }

        var now = clock.GetUtcNow();
        var previous = page.TemplateCollectionId;
        page.SetTemplate(collectionId);
        page.Touch(actor, now);
        var changes = Apply(plan.Value);
        UpdatedAtUtc = now;

        Raise(new PageTemplateChangedEvent(SiteId, pageId, collectionId, previous, now));
        RaiseAddressChanges(changes, now);
        return Result.Success();
    }

    /// <summary>
    /// Removes a page that has no children (FR-012, FR-013). Returns it, so the caller can
    /// delete its row; its draft and usage rows go with it (FR-014).
    /// </summary>
    public Result<Page> Delete(Guid pageId, int version, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Find(pageId) is not { } page)
        {
            return Result.Failure<Page>(PagesErrors.NotFound);
        }

        if (page.Version != version)
        {
            return Result.Failure<Page>(PagesErrors.OutOfDate);
        }

        var childCount = Live.Count(p => p.ParentId == pageId);
        if (childCount > 0)
        {
            return Result.Failure<Page>(PagesErrors.HasChildren(childCount));
        }

        if (page.IsHome && Live.Count > 1)
        {
            return Result.Failure<Page>(PagesErrors.HomePage);
        }

        var now = clock.GetUtcNow();
        var siblings = Children(page.ParentId);
        siblings.Remove(page);
        _pages.Remove(page);
        Renumber(siblings);
        PageCount = Live.Count;
        UpdatedAtUtc = now;

        Raise(new PageDeletedEvent(SiteId, page.Id, page.Path, now));
        return Result.Success(page);
    }

    private Page? Home => Live.Find(p => p.IsHome);

    private Page? Find(Guid pageId) => Live.Find(p => p.Id == pageId);

    /// <summary>The pages under <paramref name="parentId"/>, in order, as a new list.</summary>
    private List<Page> Children(Guid? parentId) =>
        [.. Live.Where(p => p.ParentId == parentId).OrderBy(p => p.Position)];

    private bool IsDescendant(Guid pageId, Guid of)
    {
        for (var current = Find(pageId); current?.ParentId is { } parent; current = Find(parent))
        {
            if (parent == of)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where a page goes among <paramref name="siblings"/>, which excludes it.</summary>
    private Result<int> ResolvePlacement(List<Page> siblings, Guid? parentId, Placement placement, Page? moving)
    {
        switch (placement.Kind)
        {
            case PlacementKind.First:
                return Result.Success(0);
            case PlacementKind.Last:
                return Result.Success(siblings.Count);
            default:
                var anchor = Find(placement.AfterPageId);
                if (anchor is null)
                {
                    return Result.Failure<int>(PagesErrors.ParentNotFound);
                }

                // The anchor exists but has left the target parent since the caller looked.
                if (anchor.ParentId != parentId || anchor == moving)
                {
                    return Result.Failure<int>(PagesErrors.OutOfDate);
                }

                return Result.Success(siblings.IndexOf(anchor) + 1);
        }
    }

    private static void Renumber(List<Page> siblings)
    {
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].Position != i)
            {
                siblings[i].PlaceAt(i);
            }
        }
    }

    /// <summary>
    /// Computes every page's address and depth for a proposed shape of the tree, and checks
    /// each page whose address or depth would change (research P-03).
    /// </summary>
    /// <remarks>
    /// Checks run in tree order, and the first failure wins: depth (FR-009), a reserved first
    /// segment (FR-007), length, then uniqueness (FR-006). Only changed pages are checked,
    /// because unchanged ones were valid when they were stored; but uniqueness compares
    /// against every page. Cycles cannot reach here: <see cref="Move"/> refuses them first.
    /// </remarks>
    private Result<IReadOnlyList<Planned>> Plan(Func<Page, Shape> shapeOf, Shape? added = null)
    {
        var shapes = Live.Select(shapeOf).ToList();
        if (added is not null)
        {
            shapes.Add(added);
        }

        var children = shapes.ToLookup(s => s.ParentId);
        var ordered = new List<Planned>(shapes.Count);

        void Walk(Guid? parentId, string? parentPath, bool parentIsHome, int depth)
        {
            foreach (var shape in children[parentId].OrderBy(s => s.Position))
            {
                // 016 TB-02: a template's address is a pattern, its item's slug in place of its own.
                var segment = shape.IsTemplate ? TemplateInfo.SlugPlaceholder : shape.Slug;
                var path = shape.IsHome ? "/"
                    : parentPath is null || parentIsHome ? "/" + segment
                    : parentPath + "/" + segment;
                ordered.Add(new Planned(shape.Id, path, depth));
                Walk(shape.Id, path, shape.IsHome, depth + 1);
            }
        }

        Walk(parentId: null, parentPath: null, parentIsHome: false, depth: 1);

        var current = Live.ToDictionary(p => p.Id);
        var changed = ordered
            .Where(p => !current.TryGetValue(p.Id, out var page) || page.Path != p.Path || page.Depth != p.Depth)
            .ToList();

        var occupied = ordered
            .GroupBy(p => p.Path, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var planned in changed)
        {
            if (planned.Depth > PageAddresses.MaxDepth)
            {
                return Result.Failure<IReadOnlyList<Planned>>(PagesErrors.TooDeep());
            }

            if (planned.Path != "/" && PageAddresses.IsReservedFirstSegment(FirstSegment(planned.Path)))
            {
                return Result.Failure<IReadOnlyList<Planned>>(PagesErrors.ReservedAddress(planned.Path));
            }

            if (planned.Path.Length > PageAddresses.MaxPathLength)
            {
                return Result.Failure<IReadOnlyList<Planned>>(PagesErrors.AddressTooLong(planned.Path));
            }

            if (occupied.Contains(planned.Path))
            {
                return Result.Failure<IReadOnlyList<Planned>>(PagesErrors.DuplicateAddress(planned.Path));
            }
        }

        return Result.Success<IReadOnlyList<Planned>>(changed);
    }

    /// <summary>Writes a plan's addresses and depths; returns the address changes of existing pages.</summary>
    private List<PageAddressChange> Apply(IReadOnlyList<Planned> plan)
    {
        var changes = new List<PageAddressChange>();
        foreach (var planned in plan)
        {
            var page = Find(planned.Id)!;
            if (page.Path.Length > 0 && page.Path != planned.Path)
            {
                changes.Add(new PageAddressChange(page.Id, page.Path, planned.Path));
            }

            page.SetAddress(planned.Path, planned.Depth);
        }

        return changes;
    }

    private void RaiseAddressChanges(List<PageAddressChange> changes, DateTimeOffset now)
    {
        if (changes.Count > 0)
        {
            Raise(new PageAddressesChangedEvent(SiteId, changes, now));
        }
    }

    private static string FirstSegment(string path)
    {
        var end = path.IndexOf('/', 1);
        return end < 0 ? path[1..] : path[1..end];
    }

    /// <summary>A page as a proposed change sees it: only what addresses depend on.</summary>
    private sealed record Shape(Guid Id, Guid? ParentId, string Slug, bool IsHome, int Position, bool IsTemplate = false)
    {
        public static Shape Of(Page page) => new(page.Id, page.ParentId, page.Slug, page.IsHome, page.Position, page.IsTemplate);
    }

    private sealed record Planned(Guid Id, string Path, int Depth);
}

/// <summary>Where a new or moved page goes among its siblings (research P-05).</summary>
internal readonly record struct Placement(PlacementKind Kind, Guid AfterPageId)
{
    public static Placement First => new(PlacementKind.First, Guid.Empty);

    public static Placement Last => new(PlacementKind.Last, Guid.Empty);

    public static Placement After(Guid pageId) => new(PlacementKind.After, pageId);
}

internal enum PlacementKind
{
    Last,
    First,
    After,
}
