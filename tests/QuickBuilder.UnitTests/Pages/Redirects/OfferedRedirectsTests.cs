using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.UnitTests.Pages.Redirects;

/// <summary>
/// The redirects a structural change offers, and how they are applied to a site's redirects
/// (017 FR-028 to FR-031; research SE-11; data-model §3.4).
/// </summary>
public sealed class OfferedRedirectsTests
{
    private static readonly Guid Site = Guid.Parse("0192f5a0-0000-7000-8000-0000000000aa");
    private static readonly Guid Blog = Guid.Parse("0192f5a0-0000-7000-8000-000000000001");
    private static readonly Guid Post = Guid.Parse("0192f5a0-0000-7000-8000-000000000002");
    private static readonly Guid About = Guid.Parse("0192f5a0-0000-7000-8000-000000000003");
    private static readonly Guid Team = Guid.Parse("0192f5a0-0000-7000-8000-000000000004");
    private static readonly Guid News = Guid.Parse("0192f5a0-0000-7000-8000-000000000005");
    private static readonly Guid Posts = Guid.Parse("0192f5a0-0000-7000-8000-00000000c001");

    private static TreeAddress Page(string path, Guid? parent = null) => new(path, parent, IsTemplate: false);

    private static TreeAddress Template(string pattern, Guid parent) => new(pattern, parent, IsTemplate: true);

    private static HashSet<Guid> Live(params Guid[] ids) => [.. ids];

    [Fact]
    public void A_live_page_whose_address_changed_offers_its_old_address_and_its_live_children_theirs()
    {
        var before = new Dictionary<Guid, TreeAddress> { [About] = Page("/about"), [Team] = Page("/about/team", About) };
        var after = new Dictionary<Guid, TreeAddress> { [About] = Page("/company"), [Team] = Page("/company/team", About) };

        var offers = OfferedRedirects.Plan(before, after, Live(About, Team));

        Assert.Equal(
            [
                new OfferedRedirect(RedirectMatchKinds.Exact, "/about", RedirectTarget.ToPage(About)),
                new OfferedRedirect(RedirectMatchKinds.Exact, "/about/team", RedirectTarget.ToPage(Team)),
            ],
            offers);
    }

    [Fact]
    public void A_page_that_is_not_live_offers_nothing()
    {
        var before = new Dictionary<Guid, TreeAddress> { [About] = Page("/about") };
        var after = new Dictionary<Guid, TreeAddress> { [About] = Page("/company") };

        Assert.Empty(OfferedRedirects.Plan(before, after, Live()));
    }

    [Fact]
    public void A_live_template_whose_pattern_changed_offers_one_prefix_redirect()
    {
        var before = new Dictionary<Guid, TreeAddress> { [Blog] = Page("/blog"), [Post] = Template("/blog/{slug}", Blog) };
        var after = new Dictionary<Guid, TreeAddress> { [Blog] = Page("/news"), [Post] = Template("/news/{slug}", Blog) };

        var offers = OfferedRedirects.Plan(before, after, Live(Blog, Post));

        Assert.Contains(new OfferedRedirect(RedirectMatchKinds.Prefix, "/blog", RedirectTarget.ToPath("/news")), offers);
        Assert.Contains(new OfferedRedirect(RedirectMatchKinds.Exact, "/blog", RedirectTarget.ToPage(Blog)), offers);
        Assert.Equal(2, offers.Count);
    }

    [Fact]
    public void A_template_turned_back_into_a_page_offers_nothing_for_its_items()
    {
        var before = new Dictionary<Guid, TreeAddress> { [Blog] = Page("/blog"), [Post] = Template("/blog/{slug}", Blog) };
        var after = new Dictionary<Guid, TreeAddress> { [Blog] = Page("/blog"), [Post] = Page("/blog/post", Blog) };

        Assert.Empty(OfferedRedirects.Plan(before, after, Live(Blog, Post)));
    }

    [Fact]
    public void A_live_page_made_a_template_offers_its_own_address_to_its_parent()
    {
        var before = new Dictionary<Guid, TreeAddress> { [Blog] = Page("/blog"), [Post] = Page("/blog/post", Blog) };
        var after = new Dictionary<Guid, TreeAddress> { [Blog] = Page("/blog"), [Post] = Template("/blog/{slug}", Blog) };

        Assert.Equal(
            [new OfferedRedirect(RedirectMatchKinds.Exact, "/blog/post", RedirectTarget.ToPage(Blog))],
            OfferedRedirects.Plan(before, after, Live(Blog, Post)));
    }

    [Fact]
    public void A_template_changing_collection_offers_nothing()
    {
        var addresses = new Dictionary<Guid, TreeAddress> { [Blog] = Page("/blog"), [Post] = Template("/blog/{slug}", Blog) };

        Assert.Empty(OfferedRedirects.Plan(addresses, new Dictionary<Guid, TreeAddress>(addresses), Live(Blog, Post)));
    }

    [Theory]
    [InlineData("/blog/{slug}", "/blog")]
    [InlineData("/a/b/{slug}", "/a/b")]
    [InlineData("/{slug}", null)]
    public void The_prefix_of_a_pattern_is_its_parent_address(string pattern, string? prefix)
    {
        Assert.Equal(prefix, OfferedRedirects.PrefixOf(pattern));
    }

    [Fact]
    public void An_old_address_now_held_by_live_content_is_skipped()
    {
        // /about moved to /company, and /about is now another live page's address.
        var state = State(
            new Dictionary<Guid, PageAddress> { [About] = new("/company", true, false), [Team] = new("/about", true, false) });

        var (decisions, skipped) = RedirectOffers.Decide(state, [new OfferedRedirect(RedirectMatchKinds.Exact, "/about", RedirectTarget.ToPage(About))]);

        Assert.Empty(decisions);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void Path_redirects_to_the_old_address_follow_it()
    {
        var pointing = Guid.NewGuid();
        var state = State(
            new Dictionary<Guid, PageAddress> { [About] = new("/company", true, false) },
            new RedirectEntry(pointing, RedirectMatchKinds.Exact, "/old-about", string.Empty, RedirectTarget.ToPath("/about"), true));

        var (decisions, _) = RedirectOffers.Decide(state, [new OfferedRedirect(RedirectMatchKinds.Exact, "/about", RedirectTarget.ToPage(About))]);

        Assert.Contains(new OfferDecision.Retarget(pointing, RedirectTarget.ToPage(About)), decisions);
        Assert.Single(decisions.OfType<OfferDecision.Create>());
    }

    [Fact]
    public void Moving_a_template_back_turns_its_earlier_prefix_off()
    {
        var earlier = Guid.NewGuid();
        var state = State(
            new Dictionary<Guid, PageAddress> { [Blog] = new("/blog", true, false), [News] = new("/news", true, false), [Post] = new("/blog/{slug}", true, true) },
            new RedirectEntry(earlier, RedirectMatchKinds.Prefix, "/blog", string.Empty, RedirectTarget.ToPath("/news"), true));

        var (decisions, _) = RedirectOffers.Decide(state, [new OfferedRedirect(RedirectMatchKinds.Prefix, "/news", RedirectTarget.ToPath("/blog"))]);

        Assert.Contains(new OfferDecision.TurnOff(earlier), decisions);
        Assert.Single(decisions.OfType<OfferDecision.Create>());
    }

    [Fact]
    public void An_exact_redirect_that_would_loop_is_turned_off()
    {
        // /b → /a exists; /a now moves to /b, offering /a → page at /b.
        var looping = Guid.NewGuid();
        var state = State(
            new Dictionary<Guid, PageAddress> { [About] = new("/c", true, false) },
            new RedirectEntry(looping, RedirectMatchKinds.Exact, "/c", string.Empty, RedirectTarget.ToPath("/a"), true));

        var (decisions, _) = RedirectOffers.Decide(state, [new OfferedRedirect(RedirectMatchKinds.Exact, "/a", RedirectTarget.ToPath("/c"))]);

        Assert.Contains(new OfferDecision.TurnOff(looping), decisions);
        Assert.Single(decisions.OfType<OfferDecision.Create>());
    }

    [Fact]
    public void A_source_that_already_has_a_redirect_has_it_retargeted()
    {
        var existing = Guid.NewGuid();
        var state = State(
            new Dictionary<Guid, PageAddress> { [About] = new("/company", true, false), [Team] = new("/team", true, false) },
            new RedirectEntry(existing, RedirectMatchKinds.Exact, "/about", string.Empty, RedirectTarget.ToPage(Team), true));

        var (decisions, _) = RedirectOffers.Decide(state, [new OfferedRedirect(RedirectMatchKinds.Exact, "/about", RedirectTarget.ToPage(About))]);

        Assert.Equal([new OfferDecision.Retarget(existing, RedirectTarget.ToPage(About))], decisions);
    }

    private static RedirectSiteState State(Dictionary<Guid, PageAddress> pages, params RedirectEntry[] redirects) =>
        new(
            Site,
            "https://client.example/",
            ["client.example"],
            pages,
            pages.Where(p => p.Value.IsLive && p.Value.IsTemplate).ToDictionary(_ => Posts, p => p.Value.Path),
            redirects);
}
