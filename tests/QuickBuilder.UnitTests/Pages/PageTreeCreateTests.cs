using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Events;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>Creating pages (010 US1, FR-002 to FR-007, research P-03, P-04, P-07).</summary>
public sealed class PageTreeCreateTests
{
    private readonly PageTreeTestKit _kit = new();

    [Fact]
    public void Addresses_follow_the_rule_and_the_home_page_contributes_nothing()
    {
        var home = _kit.Add("home", isHome: true);
        var services = _kit.Add("services");
        _kit.Add("hosting", services);
        _kit.Add("news", home);

        Assert.Equal(["/", "/news", "/services", "/services/hosting"], _kit.Paths());
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_new_page_goes_last_first_or_after_its_anchor()
    {
        var a = _kit.Add("a");
        _kit.Add("b");
        _kit.Add("first", placement: Placement.First);
        _kit.Add("after-a", placement: Placement.After(a.Id));

        Assert.Equal(["/first", "/a", "/after-a", "/b"], _kit.Paths());
        _kit.AssertInvariants();
    }

    [Fact]
    public void The_same_slug_is_allowed_under_different_parents()
    {
        _kit.Add("faq", _kit.Add("services"));
        _kit.Add("faq", _kit.Add("support"));

        Assert.Contains("/services/faq", _kit.Paths());
        Assert.Contains("/support/faq", _kit.Paths());
    }

    [Fact]
    public void A_top_level_page_and_a_child_of_the_home_page_cannot_share_a_slug()
    {
        var home = _kit.Add("home", isHome: true);
        _kit.Add("about");

        var created = _kit.Tree.CreatePage("About", "about", home.Id, Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.duplicate_address", created.Error?.Code);
        Assert.Contains("/about", created.Error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("health")]
    public void Reserved_first_segments_are_refused_but_allowed_deeper(string slug)
    {
        var refused = _kit.Tree.CreatePage("x", slug, null, Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);
        Assert.Equal("pages.reserved_address", refused.Error?.Code);

        _kit.Add(slug, _kit.Add("docs"));
        Assert.Contains($"/docs/{slug}", _kit.Paths());
    }

    [Theory]
    [InlineData("About", "pages.invalid_slug")]
    [InlineData("-a", "pages.invalid_slug")]
    [InlineData("a--b", "pages.invalid_slug")]
    [InlineData("a_b", "pages.invalid_slug")]
    [InlineData("", "pages.invalid_slug")]
    public void Slugs_outside_the_grammar_are_refused(string slug, string code)
    {
        var created = _kit.Tree.CreatePage("x", slug, null, Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);
        Assert.Equal(code, created.Error?.Code);
        Assert.Empty(_kit.Tree.Pages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_title_is_refused(string title)
    {
        var created = _kit.Tree.CreatePage(title, "a", null, Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);
        Assert.Equal("pages.invalid_title", created.Error?.Code);
    }

    [Fact]
    public void A_second_home_page_and_a_nested_home_page_are_refused()
    {
        var home = _kit.Add("home", isHome: true);

        var second = _kit.Tree.CreatePage("x", "x", null, Placement.Last, true, PageTreeTestKit.Actor, _kit.Clock);
        var nested = _kit.Tree.CreatePage("y", "y", home.Id, Placement.Last, true, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.home_page_exists", second.Error?.Code);
        Assert.Equal("pages.home_page_must_be_top_level", nested.Error?.Code);
    }

    [Fact]
    public void Nesting_stops_at_ten_levels()
    {
        Page? parent = null;
        for (var level = 1; level <= 10; level++)
        {
            parent = _kit.Add($"l{level}", parent);
        }

        var eleventh = _kit.Tree.CreatePage("x", "l11", parent!.Id, Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.too_deep", eleventh.Error?.Code);
        _kit.AssertInvariants();
    }

    [Fact]
    public void A_site_holds_at_most_2000_pages()
    {
        for (var i = 0; i < 2000; i++)
        {
            _kit.Add($"p{i}");
        }

        var created = _kit.Tree.CreatePage("x", "one-more", null, Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.too_many_pages", created.Error?.Code);
        Assert.Equal(2000, _kit.Tree.PageCount);
    }

    /// <remarks>
    /// Ten levels of 100-character slugs make 1,010 characters, so the depth and slug limits
    /// already keep every address under 1,024: <c>pages.address_too_long</c> is a backstop
    /// for limits that may be raised later, and this pins that the longest address the
    /// current limits allow is accepted.
    /// </remarks>
    [Fact]
    public void The_longest_address_the_limits_allow_fits()
    {
        Page? parent = null;
        for (var level = 0; level < 10; level++)
        {
            parent = _kit.Add(new string((char)('a' + level), 100), parent);
        }

        Assert.Equal(1010, parent!.Path.Length);
        _kit.AssertInvariants();
    }

    [Fact]
    public void An_unknown_parent_or_anchor_is_refused_and_an_anchor_elsewhere_is_out_of_date()
    {
        var a = _kit.Add("a");
        var child = _kit.Add("child", a);

        var noParent = _kit.Tree.CreatePage("x", "x", Guid.NewGuid(), Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);
        var noAnchor = _kit.Tree.CreatePage("x", "x", null, Placement.After(Guid.NewGuid()), false, PageTreeTestKit.Actor, _kit.Clock);
        var moved = _kit.Tree.CreatePage("x", "x", null, Placement.After(child.Id), false, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Equal("pages.parent_not_found", noParent.Error?.Code);
        Assert.Equal("pages.parent_not_found", noAnchor.Error?.Code);
        Assert.Equal("pages.out_of_date", moved.Error?.Code);
    }

    [Fact]
    public void Creating_raises_page_created_and_home_page_changed()
    {
        var home = _kit.Add("home", isHome: true);
        var page = _kit.Add("about");

        Assert.Collection(
            _kit.Tree.DomainEvents,
            e => Assert.Equal(home.Id, Assert.IsType<PageCreatedEvent>(e).PageId),
            e => Assert.Equal(new HomePageChangedEvent(_kit.Tree.SiteId, home.Id, null, ((HomePageChangedEvent)e).OccurredAtUtc), e),
            e => Assert.Equal("/about", Assert.IsType<PageCreatedEvent>(e).Path));
        Assert.Equal(1, page.Version);
    }

    [Fact]
    public void A_refused_create_changes_nothing()
    {
        _kit.Add("a");
        _kit.Tree.ClearDomainEvents();

        _kit.Tree.CreatePage("x", "a", null, Placement.Last, false, PageTreeTestKit.Actor, _kit.Clock);

        Assert.Single(_kit.Tree.Pages);
        Assert.Empty(_kit.Tree.DomainEvents);
    }
}
