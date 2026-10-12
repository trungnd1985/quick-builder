using QuickBuilder.Pages.Domain;
using QuickBuilder.UnitTests.Sites;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>A tree to act on, and shorthand for the pages in it.</summary>
internal sealed class PageTreeTestKit
{
    public static readonly Guid Actor = Guid.Parse("0192f000-0000-7000-8000-000000000001");

    public PageTreeTestKit()
    {
        Clock = new SteppedClock(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
        Tree = PageTree.Start(Guid.Parse("0192f000-0000-7000-8000-0000000000aa"), Clock);
    }

    public SteppedClock Clock { get; }

    public PageTree Tree { get; }

    /// <summary>Adds a page and asserts it was accepted.</summary>
    public Page Add(string slug, Page? parent = null, bool isHome = false, Placement? placement = null)
    {
        var created = Tree.CreatePage(Title(slug), slug, parent?.Id, placement ?? Placement.Last, isHome, Actor, Clock);
        Assert.True(created.IsSuccess, created.Error?.ToString());
        return created.Value;
    }

    public Page At(string path) => Tree.Pages.Single(p => p.Path == path);

    /// <summary>Every page's address, in tree order.</summary>
    public string[] Paths() => Ordered().Select(p => p.Path).ToArray();

    /// <summary>The pages in depth-first order, parents before children, siblings by position.</summary>
    public List<Page> Ordered()
    {
        var ordered = new List<Page>();
        void Walk(Guid? parent)
        {
            foreach (var page in Tree.Pages.Where(p => p.ParentId == parent).OrderBy(p => p.Position))
            {
                ordered.Add(page);
                Walk(page.Id);
            }
        }

        Walk(null);
        return ordered;
    }

    /// <summary>Every invariant in data-model.md, "Invariants of PageTree".</summary>
    public void AssertInvariants()
    {
        var pages = Tree.Pages;
        var byId = pages.ToDictionary(p => p.Id);

        // 1. Unique addresses.
        Assert.Equal(pages.Count, pages.Select(p => p.Path).Distinct(StringComparer.Ordinal).Count());

        // 2. At most one home page, top level, at "/".
        var homes = pages.Where(p => p.IsHome).ToList();
        Assert.True(homes.Count <= 1);
        Assert.All(homes, h => Assert.True(h.ParentId is null && h.Path == "/"));

        // 3 and 4. Every address and depth follow from the ancestors' slugs; no page is its own ancestor.
        foreach (var page in pages)
        {
            var seen = new HashSet<Guid> { page.Id };
            var depth = 1;
            for (var parent = page.ParentId; parent is { } p; parent = byId[p].ParentId)
            {
                Assert.True(seen.Add(p), $"{page.Path} is its own ancestor.");
                depth++;
            }

            Assert.Equal(depth, page.Depth);
            Assert.Equal(ExpectedPath(page, byId), page.Path);
        }

        // 5. Limits.
        Assert.All(pages, p => Assert.InRange(p.Depth, 1, 10));
        Assert.Equal(pages.Count, Tree.PageCount);

        // 6. Contiguous positions under each parent.
        foreach (var group in pages.GroupBy(p => p.ParentId))
        {
            Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(p => p.Position).Order());
        }

        // 7 and 8. No reserved first segment, no over-long address.
        Assert.All(pages, p => Assert.DoesNotContain(p.Path.Split('/')[1], new[] { "api", "admin", "health" }));
        Assert.All(pages, p => Assert.True(p.Path.Length <= 1024));
    }

    private static string ExpectedPath(Page page, Dictionary<Guid, Page> byId)
    {
        if (page.IsHome)
        {
            return "/";
        }

        if (page.ParentId is not { } parentId)
        {
            return "/" + page.Slug;
        }

        // 016 TB-02: a template's address is a pattern, its items' slug in place of its own.
        var segment = page.IsTemplate ? "{slug}" : page.Slug;
        var parent = byId[parentId];
        return parent.IsHome ? "/" + segment : ExpectedPath(parent, byId) + "/" + segment;
    }

    public static string Title(string slug) => $"Title of {slug}";
}
