using QuickBuilder.Pages.Domain.Redirects;

namespace QuickBuilder.UnitTests.Pages.Redirects;

/// <summary>
/// Sources, targets, chains and loops (017 FR-017, FR-017a, FR-033; research SE-09).
/// </summary>
public sealed class RedirectRulesTests
{
    [Theory]
    [InlineData("/About-Us.html", "exact", "/about-us.html", "")]
    [InlineData("/old/", "exact", "/old", "")]
    [InlineData("/caf%C3%A9", "exact", "/café", "")]
    [InlineData("/product.php?id=7", "exact", "/product.php", "id=7")]
    [InlineData("/?p=12", "exact", "/", "p=12")]
    [InlineData("/blog", "prefix", "/blog", "")]
    [InlineData("  /spaced  ", "exact", "/spaced", "")]
    public void A_source_is_stored_normalised(string input, string match, string path, string query)
    {
        var parsed = RedirectSource.Parse(input, match);

        Assert.True(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.Message : null);
        Assert.Equal((path, query), (parsed.Value.Path, parsed.Value.Query));
    }

    [Theory]
    [InlineData("", "exact")]
    [InlineData("about", "exact")]
    [InlineData("//elsewhere.test/x", "exact")]
    [InlineData("/a/../b", "exact")]
    [InlineData("/a//b", "exact")]
    [InlineData("/a\\b", "exact")]
    [InlineData("/", "exact")]
    [InlineData("/", "prefix")]
    [InlineData("/api/pages", "exact")]
    [InlineData("/ADMIN", "exact")]
    [InlineData("/health/live", "exact")]
    [InlineData("/_media/x.png", "exact")]
    [InlineData("/_assets/blocks.css", "exact")]
    [InlineData("/robots.txt", "exact")]
    [InlineData("/sitemap.xml", "exact")]
    [InlineData("/sitemap-2.xml", "exact")]
    [InlineData("/blog?page=2", "prefix")]
    public void Some_sources_are_refused(string input, string match)
    {
        var parsed = RedirectSource.Parse(input, match);

        Assert.True(parsed.IsFailure);
        Assert.Equal("redirects.source_invalid", parsed.Error.Code);
    }

    [Fact]
    public void A_source_over_the_lengths_is_refused()
    {
        Assert.True(RedirectSource.Parse("/" + new string('a', 1024), "exact").IsFailure);
        Assert.True(RedirectSource.Parse("/x?q=" + new string('a', 512), "exact").IsFailure);
    }

    [Theory]
    [InlineData("https://elsewhere.test/page")]
    [InlineData("http://elsewhere.test/")]
    [InlineData("https://elsewhere.test/?q=1#top")]
    public void Absolute_http_addresses_are_allowed_targets(string url)
    {
        Assert.True(RedirectTargets.Url(url).IsSuccess);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://elsewhere.test/")]
    [InlineData("//elsewhere.test/")]
    [InlineData("https://user:pw@elsewhere.test/")]
    [InlineData("https://elsewhere.test/a b")]
    [InlineData("relative/path")]
    public void Other_addresses_are_refused_as_targets(string url)
    {
        var target = RedirectTargets.Url(url);

        Assert.True(target.IsFailure);
        Assert.Equal("redirects.target_invalid", target.Error.Code);
    }

    [Theory]
    [InlineData("/new", true)]
    [InlineData("/new?x=1", true)]
    [InlineData("//elsewhere.test", false)]
    [InlineData("/a\\b", false)]
    [InlineData("new", false)]
    public void Site_paths_start_with_one_slash(string path, bool valid)
    {
        Assert.Equal(valid, RedirectTargets.SitePath(path).IsSuccess);
    }

    [Fact]
    public void A_prefix_sends_only_to_a_page_or_a_path()
    {
        Assert.True(RedirectTargets.CheckForMatch(RedirectTarget.ToUrl("https://elsewhere.test/"), RedirectMatchKinds.Prefix).IsFailure);
        Assert.True(RedirectTargets.CheckForMatch(RedirectTarget.ToItem(Guid.NewGuid(), Guid.NewGuid()), RedirectMatchKinds.Prefix).IsFailure);
        Assert.True(RedirectTargets.CheckForMatch(RedirectTarget.ToPath("/new"), RedirectMatchKinds.Prefix).IsSuccess);
        Assert.True(RedirectTargets.CheckForMatch(RedirectTarget.ToPage(Guid.NewGuid()), RedirectMatchKinds.Prefix).IsSuccess);
    }

    [Fact]
    public void A_target_that_is_another_redirect_s_source_is_replaced_by_the_final_target()
    {
        var graph = new FakeGraph()
            .Exact("/b", RedirectTarget.ToPath("/c"))
            .Exact("/c", RedirectTarget.ToPath("/d"));

        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/a", string.Empty, RedirectTarget.ToPath("/b"), graph);

        Assert.False(check.IsLoop);
        Assert.True(check.Shortened);
        Assert.Equal(RedirectTarget.ToPath("/d"), check.FinalTarget);
    }

    [Fact]
    public void A_redirect_back_to_its_own_source_is_a_loop()
    {
        var graph = new FakeGraph();

        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/a", string.Empty, RedirectTarget.ToPath("/a"), graph);

        Assert.True(check.IsLoop);
    }

    [Fact]
    public void A_chain_that_comes_back_is_a_loop_naming_the_redirect_that_closes_it()
    {
        var graph = new FakeGraph().Exact("/b", RedirectTarget.ToPath("/a"), out var closing);

        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/a", string.Empty, RedirectTarget.ToPath("/b"), graph);

        Assert.True(check.IsLoop);
        Assert.Equal(closing, check.LoopWith);
    }

    [Fact]
    public void A_query_source_may_send_to_its_own_path()
    {
        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/", "p=12", RedirectTarget.ToPath("/"), new FakeGraph());

        Assert.False(check.IsLoop);
    }

    [Fact]
    public void A_prefix_that_would_match_its_own_target_is_a_loop()
    {
        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Prefix, "/blog", string.Empty, RedirectTarget.ToPath("/blog/new"), new FakeGraph());

        Assert.True(check.IsLoop);
    }

    [Fact]
    public void A_chain_through_a_prefix_is_followed_for_loops_but_not_shortened()
    {
        var graph = new FakeGraph().Prefix("/old", RedirectTarget.ToPath("/a"), out var prefix);

        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/a/x", string.Empty, RedirectTarget.ToPath("/old/x"), graph);

        Assert.True(check.IsLoop);
        Assert.Equal(prefix, check.LoopWith);
    }

    [Fact]
    public void Live_content_ends_a_chain_because_it_wins_over_redirects()
    {
        var graph = new FakeGraph().Exact("/b", RedirectTarget.ToPath("/a")).Live("/b");

        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/a", string.Empty, RedirectTarget.ToPath("/b"), graph);

        Assert.False(check.IsLoop);
        Assert.False(check.Shortened);
    }

    [Fact]
    public void A_page_target_is_followed_at_its_current_address()
    {
        var page = Guid.NewGuid();
        var graph = new FakeGraph().Page(page, "/about").Exact("/about", RedirectTarget.ToPath("/old"));

        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/old", string.Empty, RedirectTarget.ToPage(page), graph);

        Assert.True(check.IsLoop);
    }

    [Fact]
    public void An_absolute_target_ends_the_chain()
    {
        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/a", string.Empty, RedirectTarget.ToUrl("https://elsewhere.test/a"), new FakeGraph());

        Assert.False(check.IsLoop);
        Assert.False(check.Shortened);
    }

    [Fact]
    public void A_redirect_being_changed_does_not_meet_itself()
    {
        var graph = new FakeGraph().Exact("/a", RedirectTarget.ToPath("/b"), out var self);

        var check = RedirectRules.CheckChain(self, RedirectMatchKinds.Exact, "/a", string.Empty, RedirectTarget.ToPath("/c"), graph);

        Assert.False(check.IsLoop);
    }

    [Fact]
    public void A_chain_longer_than_ten_steps_is_treated_as_a_loop()
    {
        var graph = new FakeGraph();
        for (var i = 1; i <= 12; i++)
        {
            graph.Exact($"/s{i}", RedirectTarget.ToPath($"/s{i + 1}"));
        }

        var check = RedirectRules.CheckChain(null, RedirectMatchKinds.Exact, "/start", string.Empty, RedirectTarget.ToPath("/s1"), graph);

        Assert.True(check.IsLoop);
    }

    /// <summary>A site's redirects, pages and live addresses, in memory.</summary>
    private sealed class FakeGraph : IRedirectGraph
    {
        private readonly List<(Guid Id, string Match, string Source, RedirectTarget Target)> _redirects = [];
        private readonly Dictionary<Guid, string> _pages = [];
        private readonly HashSet<string> _live = new(StringComparer.Ordinal);

        public FakeGraph Exact(string source, RedirectTarget target) => Exact(source, target, out _);

        public FakeGraph Exact(string source, RedirectTarget target, out Guid id)
        {
            id = Guid.NewGuid();
            _redirects.Add((id, RedirectMatchKinds.Exact, source, target));
            return this;
        }

        public FakeGraph Prefix(string source, RedirectTarget target, out Guid id)
        {
            id = Guid.NewGuid();
            _redirects.Add((id, RedirectMatchKinds.Prefix, source, target));
            return this;
        }

        public FakeGraph Page(Guid id, string path)
        {
            _pages[id] = path;
            return this;
        }

        public FakeGraph Live(string path)
        {
            _live.Add(path);
            return this;
        }

        public string? AddressOf(RedirectTarget target) => target.Kind switch
        {
            RedirectTargetKinds.Path => target.Path!.Split('?')[0],
            RedirectTargetKinds.Page => _pages.GetValueOrDefault(target.PageId!.Value),
            _ => null,
        };

        public bool IsLiveContent(string path) => _live.Contains(path);

        public RedirectHop? Follow(string path, Guid? except)
        {
            var exact = _redirects.FirstOrDefault(r => r.Id != except && r.Match == RedirectMatchKinds.Exact && r.Source == path);
            if (exact.Id != Guid.Empty)
            {
                return new RedirectHop(exact.Id, exact.Target, string.Empty, IsExact: true);
            }

            var prefix = _redirects
                .Where(r => r.Id != except && r.Match == RedirectMatchKinds.Prefix && path.StartsWith(r.Source + "/", StringComparison.Ordinal))
                .OrderByDescending(r => r.Source.Length)
                .FirstOrDefault();
            return prefix.Id == Guid.Empty ? null : new RedirectHop(prefix.Id, prefix.Target, path[prefix.Source.Length..], IsExact: false);
        }
    }
}
