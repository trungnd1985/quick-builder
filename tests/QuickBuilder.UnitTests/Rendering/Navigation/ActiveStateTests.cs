using QuickBuilder.Rendering.Rendering.Navigation;

namespace QuickBuilder.UnitTests.Rendering.Navigation;

/// <summary>
/// Which entries are the current page, contain it, or are its section (024 FR-027 to FR-030,
/// research NV-11, SC-004; tasks T066). The rules read on resolved entries: hidden entries and
/// targets that are not live are already gone or unlinked by then.
/// </summary>
public sealed class ActiveStateTests
{
    private static readonly Guid Home = Guid.Parse("0192f5a0-0000-7000-8000-000000000001");
    private static readonly Guid Products = Guid.Parse("0192f5a0-0000-7000-8000-000000000002");
    private static readonly Guid Chairs = Guid.Parse("0192f5a0-0000-7000-8000-000000000003");
    private static readonly Guid Oak = Guid.Parse("0192f5a0-0000-7000-8000-000000000004");
    private static readonly Guid Blog = Guid.Parse("0192f5a0-0000-7000-8000-000000000005");
    private static readonly Guid Template = Guid.Parse("0192f5a0-0000-7000-8000-000000000006");
    private static readonly Guid Post = Guid.Parse("0192f5a0-0000-7000-8000-000000000007");
    private static readonly Guid About = Guid.Parse("0192f5a0-0000-7000-8000-000000000008");

    private static ResolvedEntry Page(string id, Guid pageId, string path, string? anchor = null, params ResolvedEntry[] children) =>
        new() { Id = id, Label = id, Href = path + anchor, PageId = pageId, Path = path, Children = [.. children] };

    private static ResolvedEntry Item(string id, Guid itemId, string path) =>
        new() { Id = id, Label = id, Href = path, ItemId = itemId };

    private static ResolvedEntry Group(string id, params ResolvedEntry[] children) =>
        new() { Id = id, Label = id, Children = [.. children] };

    /// <summary>
    /// Home; Shop (group) > Products > Chairs > Oak; Blog; a post; About#team; Status (external);
    /// Contact (#contact).
    /// </summary>
    private static List<ResolvedEntry> Menu() =>
    [
        Page("home", Home, "/"),
        Group("shop", Page("products", Products, "/products", null, Page("chairs", Chairs, "/products/chairs", null, Page("oak", Oak, "/products/chairs/oak")))),
        Page("blog", Blog, "/blog"),
        Item("post", Post, "/blog/hello"),
        Page("about", About, "/about", "#team"),
        new() { Id = "status", Label = "Status", Href = "https://status.example/", External = true },
        new() { Id = "contact", Label = "Contact", Href = "#contact" },
    ];

    private static Dictionary<string, NavState> Mark(List<ResolvedEntry> menu, CurrentPage current)
    {
        ActiveState.Mark(menu, current);
        var states = new Dictionary<string, NavState>();
        void Walk(IEnumerable<ResolvedEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.State != NavState.None)
                {
                    states[entry.Id] = entry.State;
                }

                Walk(entry.Children);
            }
        }

        Walk(menu);
        return states;
    }

    private static Dictionary<string, NavState> States(params (string Id, NavState State)[] expected) =>
        expected.ToDictionary(e => e.Id, e => e.State);

    [Fact]
    public void A_nested_page_is_current_and_every_entry_above_it_contains_it()
    {
        Assert.Equal(
            States(("shop", NavState.Containing), ("products", NavState.Containing), ("chairs", NavState.Containing), ("oak", NavState.Current)),
            Mark(Menu(), new CurrentPage(Oak, null, "/products/chairs/oak")));
    }

    [Fact]
    public void A_page_missing_from_the_menu_marks_its_nearest_section_page_instead()
    {
        // /products/chairs/pine is not in the menu: Chairs is the longest prefix, segment by segment.
        Assert.Equal(
            States(("shop", NavState.Containing), ("products", NavState.Containing), ("chairs", NavState.Section)),
            Mark(Menu(), new CurrentPage(Guid.NewGuid(), null, "/products/chairs/pine")));

        // "/products-old" shares characters with "/products" but not a segment.
        Assert.Empty(Mark(Menu(), new CurrentPage(Guid.NewGuid(), null, "/products-old")));
    }

    [Fact]
    public void The_home_page_is_current_on_its_own_address_and_a_section_of_nothing()
    {
        Assert.Equal(States(("home", NavState.Current)), Mark(Menu(), new CurrentPage(Home, null, "/")));
        Assert.Empty(Mark(Menu(), new CurrentPage(Guid.NewGuid(), null, "/pricing")));
    }

    [Fact]
    public void An_item_page_is_current_through_its_entry_or_falls_back_to_its_section()
    {
        Assert.Equal(States(("post", NavState.Current)), Mark(Menu(), new CurrentPage(Template, Post, "/blog/hello")));

        // Another item of the same template: no entry, so Blog is its section.
        Assert.Equal(States(("blog", NavState.Section)), Mark(Menu(), new CurrentPage(Template, Guid.NewGuid(), "/blog/other")));
    }

    [Fact]
    public void A_paged_list_and_a_page_linked_with_an_anchor_are_still_current()
    {
        // The current address carries no query: page 2 of the blog is the blog.
        Assert.Equal(States(("blog", NavState.Current)), Mark(Menu(), new CurrentPage(Blog, null, "/blog")));
        Assert.Equal(States(("about", NavState.Current)), Mark(Menu(), new CurrentPage(About, null, "/about")));
    }

    [Fact]
    public void The_same_target_twice_marks_both_entries()
    {
        var menu = Menu();
        menu.Add(Group("footer", Page("products-again", Products, "/products")));

        var states = Mark(menu, new CurrentPage(Products, null, "/products"));

        Assert.Equal(NavState.Current, states["products"]);
        Assert.Equal(NavState.Current, states["products-again"]);
        Assert.Equal(NavState.Containing, states["footer"]);
        Assert.Equal(NavState.Containing, states["shop"]);
    }

    [Fact]
    public void Addresses_anchors_and_groups_are_never_current_or_sections()
    {
        var menu = new List<ResolvedEntry>
        {
            new() { Id = "site-url", Label = "Products", Href = "/products" },
            new() { Id = "external", Label = "Status", Href = "https://example.test/products", External = true },
            new() { Id = "anchor", Label = "Top", Href = "#top" },
            Group("group"),
        };

        Assert.Empty(Mark(menu, new CurrentPage(Products, null, "/products")));
        Assert.Empty(Mark(menu, new CurrentPage(Chairs, null, "/products/chairs")));
    }

    [Fact]
    public void An_entry_that_does_not_link_is_never_current()
    {
        // A page that is not live is resolved without a link; a hidden entry is not resolved at all.
        var menu = new List<ResolvedEntry> { new() { Id = "draft", Label = "Draft", PageId = Products, Path = "/products" } };

        Assert.Empty(Mark(menu, new CurrentPage(Products, null, "/products")));
        Assert.Empty(Mark(menu, new CurrentPage(Chairs, null, "/products/chairs")));
    }

    [Fact]
    public void A_current_entry_below_the_shown_levels_still_marks_its_ancestors()
    {
        var menu = Menu();
        ActiveState.Mark(menu, new CurrentPage(Oak, null, "/products/chairs/oak"));

        // Two levels shown: Oak (level 4 of the tree here) and Chairs are gone, Products stays.
        var shown = MenuModel.Prune(menu, levels: 2);

        var shop = Assert.Single(shown, e => e.Id == "shop");
        Assert.Equal(NavState.Containing, shop.State);
        var products = Assert.Single(shop.Children);
        Assert.Equal(NavState.Containing, products.State);
        Assert.Empty(products.Children);
        Assert.All(shown.Where(e => e.Id != "shop"), e => Assert.Equal(NavState.None, e.State));
    }
}
