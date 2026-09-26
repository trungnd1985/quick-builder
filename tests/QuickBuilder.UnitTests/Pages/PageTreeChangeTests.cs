using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>Renaming, moving and choosing the home page (010 US2, FR-008 to FR-010, research P-05, P-07).</summary>
public sealed class PageTreeChangeTests
{
    private readonly PageTreeTestKit _kit = new();

    private PageTree Tree => _kit.Tree;

    [Fact]
    public void A_title_change_keeps_the_slug_and_address()
    {
        var about = _kit.Add("about");

        var renamed = Tree.Rename(about.Id, "About us", "about", about.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.True(renamed.IsSuccess);
        Assert.Equal(("About us", "/about", 2), (about.Title, about.Path, about.Version));
        Assert.DoesNotContain(Tree.DomainEvents, e => e is PageAddressesChangedEvent);
    }

    [Fact]
    public void A_new_slug_recomputes_the_whole_subtree_in_one_event()
    {
        var services = _kit.Add("services");
        var hosting = _kit.Add("hosting", services);
        _kit.Add("plans", hosting);
        Tree.ClearDomainEvents();

        Tree.Rename(services.Id, "What we do", "what-we-do", services.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal(["/what-we-do", "/what-we-do/hosting", "/what-we-do/hosting/plans"], _kit.Paths());
        var changed = Assert.IsType<PageAddressesChangedEvent>(Assert.Single(Tree.DomainEvents));
        Assert.Equal(3, changed.Changes.Count);
        Assert.Contains(changed.Changes, c => c.OldPath == "/services/hosting/plans" && c.NewPath == "/what-we-do/hosting/plans");
        _kit.AssertInvariants();
    }

    [Fact]
    public void Only_the_page_acted_on_gets_a_new_version()
    {
        var services = _kit.Add("services");
        var hosting = _kit.Add("hosting", services);
        var contact = _kit.Add("contact");

        Tree.Rename(services.Id, "Services", "what-we-do", services.Version, PageTreeTestKit.Actor, _kit.Clock);
        Tree.Move(contact.Id, null, null, contact.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal((2, 1), (services.Version, hosting.Version));
        Assert.Equal(2, contact.Version);
    }

    [Fact]
    public void A_reslug_that_would_clash_is_refused_whole_and_names_the_path()
    {
        var services = _kit.Add("services");
        _kit.Add("faq", services);
        var support = _kit.Add("support");
        _kit.Add("faq", support);
        _kit.Add("services-faq");

        var clash = Tree.Rename(support.Id, "x", "services", support.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.duplicate_address", clash.Error?.Code);
        Assert.Contains("/services", clash.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("/support/faq", _kit.Paths());
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_stale_version_is_refused()
    {
        var about = _kit.Add("about");
        Tree.Rename(about.Id, "About", "about-us", about.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.out_of_date", Tree.Rename(about.Id, "x", "x", 1, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);
        Assert.Equal("pages.out_of_date", Tree.Move(about.Id, null, null, 1, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);
        Assert.Equal("pages.out_of_date", Tree.MakeHome(about.Id, 1, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);
        Assert.Equal("pages.out_of_date", Tree.Delete(about.Id, 1, _kit.Clock).Error?.Code);
    }

    [Fact]
    public void Moving_takes_the_subtree_and_recomputes_it()
    {
        var about = _kit.Add("about");
        var team = _kit.Add("team", about);
        var services = _kit.Add("services");
        var hosting = _kit.Add("hosting", services);
        _kit.Add("plans", hosting);

        var moved = Tree.Move(hosting.Id, about.Id, team.Id, hosting.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.True(moved.IsSuccess);
        Assert.Equal(["/about", "/about/team", "/about/hosting", "/about/hosting/plans", "/services"], _kit.Paths());
        _kit.AssertInvariants();
    }

    [Fact]
    public void Reordering_siblings_changes_no_address_and_raises_no_address_event()
    {
        _kit.Add("about");
        _kit.Add("services");
        var contact = _kit.Add("contact");
        Tree.ClearDomainEvents();

        Tree.Move(contact.Id, null, null, contact.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal(["/contact", "/about", "/services"], _kit.Paths());
        Assert.Empty(Tree.DomainEvents);
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_move_to_where_the_page_already_is_changes_nothing()
    {
        var a = _kit.Add("a");
        var b = _kit.Add("b");

        Assert.True(Tree.Move(b.Id, null, a.Id, b.Version, PageTreeTestKit.Actor, _kit.Clock).IsSuccess);
        Assert.True(Tree.Move(b.Id, null, b.Id, b.Version, PageTreeTestKit.Actor, _kit.Clock).IsSuccess);

        Assert.Equal(1, b.Version);
        Assert.Equal(["/a", "/b"], _kit.Paths());
    }

    [Fact]
    public void A_page_cannot_move_under_itself_or_its_descendants()
    {
        var about = _kit.Add("about");
        var team = _kit.Add("team", about);

        Assert.Equal("pages.move_into_descendant", Tree.Move(about.Id, about.Id, null, 1, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);
        Assert.Equal("pages.move_into_descendant", Tree.Move(about.Id, team.Id, null, 1, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);
    }

    [Fact]
    public void A_move_that_puts_the_subtree_too_deep_is_refused()
    {
        Page? parent = null;
        for (var level = 1; level <= 9; level++)
        {
            parent = _kit.Add($"l{level}", parent);
        }

        var branch = _kit.Add("branch");
        _kit.Add("leaf", branch);

        var moved = Tree.Move(branch.Id, parent!.Id, null, branch.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.too_deep", moved.Error?.Code);
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_move_whose_anchor_left_the_target_parent_is_out_of_date()
    {
        var a = _kit.Add("a");
        var b = _kit.Add("b");
        var c = _kit.Add("c", a);

        var moved = Tree.Move(b.Id, a.Id, a.Id, b.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.out_of_date", moved.Error?.Code);
        Assert.True(Tree.Move(b.Id, a.Id, c.Id, b.Version, PageTreeTestKit.Actor, _kit.Clock).IsSuccess);
    }

    [Fact]
    public void The_home_page_cannot_move_under_a_parent()
    {
        var home = _kit.Add("home", isHome: true);
        var about = _kit.Add("about");

        Assert.Equal(
            "pages.home_page_must_be_top_level",
            Tree.Move(home.Id, about.Id, null, home.Version, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);
    }

    [Fact]
    public void Make_home_swaps_both_subtrees_addresses()
    {
        var home = _kit.Add("home", isHome: true);
        _kit.Add("news", home);
        var about = _kit.Add("about");
        _kit.Add("team", about);
        Tree.ClearDomainEvents();

        var made = Tree.MakeHome(about.Id, about.Version, PageTreeTestKit.Actor, _kit.Clock);

        Assert.True(made.IsSuccess);
        Assert.Equal(["/home", "/home/news", "/", "/team"], _kit.Paths());
        Assert.Equal((2, 2), (home.Version, about.Version));
        Assert.Contains(Tree.DomainEvents, e => e is HomePageChangedEvent { PreviousPageId: { } previous } && previous == home.Id);
        Assert.Contains(Tree.DomainEvents, e => e is PageAddressesChangedEvent { Changes.Count: 4 });
        _kit.AssertInvariants();
    }

    [Fact]
    public void Make_home_is_refused_whole_when_an_address_would_clash()
    {
        // The home page keeps "about" in reserve while a top-level page is /about (P-07).
        var home = _kit.Add("about", isHome: true);
        _kit.Add("about");
        var contact = _kit.Add("contact");

        var made = Tree.MakeHome(contact.Id, contact.Version, PageTreeTestKit.Actor, _kit.Clock);

        // The former home page would get /about back, which is taken.
        Assert.Equal("pages.duplicate_address", made.Error?.Code);
        Assert.True(home.IsHome);
        Assert.False(contact.IsHome);
        _kit.AssertInvariants();
    }

    [Fact]
    public void Make_home_on_a_child_is_refused_and_on_the_home_page_changes_nothing()
    {
        var home = _kit.Add("home", isHome: true);
        var child = _kit.Add("child", _kit.Add("about"));

        Assert.Equal("pages.home_page_must_be_top_level", Tree.MakeHome(child.Id, child.Version, PageTreeTestKit.Actor, _kit.Clock).Error?.Code);
        Assert.True(Tree.MakeHome(home.Id, home.Version, PageTreeTestKit.Actor, _kit.Clock).IsSuccess);
        Assert.Equal(1, home.Version);
    }
}
