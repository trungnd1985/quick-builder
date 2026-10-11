using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Paginated lists on a published page: every item on exactly one page, two lists paging
/// independently, plain links, bounded cache keys, and the way back from past the last page
/// (016 US5, research TB-11, quickstart §5; tasks T072). The pager's markup itself is pinned by
/// the unit golden <c>tests/fixtures/rendering/lists/paged.html</c>.
/// </summary>
/// <remarks>
/// <c>/blog</c> lists 40 posts, 10 per page under <c>page</c> (its <c>limit</c> of 12 is ignored),
/// and 5 people, 2 per page under <c>people</c>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed partial class PagingTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "paging");
        for (var n = 1; n <= 40; n++)
        {
            await _world.PublishedPostAsync($"post-{n:00}", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Post {n:00}" });
        }

        for (var n = 1; n <= 5; n++)
        {
            await _world.PublishedPersonAsync($"Person {n}");
        }

        var blog = await _world.PageAsync("blog");
        await _world.PublishDocumentAsync(blog, TemplatesWorld.Document(
            TemplatesWorld.ListOf(
                "posts",
                _world.Posts,
                """ "sort":[{"item":"slug","dir":"asc"}],"limit":12,"paging":{"pageSize":10},"layerLabel":"Posts" """,
                TemplatesWorld.BoundHeading("p-title", """{"field":"f1"}""")),
            TemplatesWorld.ListOf(
                "people",
                _world.People,
                """ "sort":[{"item":"slug","dir":"asc"}],"paging":{"pageSize":2,"param":"people"} """,
                TemplatesWorld.BoundHeading("n-title", """{"field":"f1"}"""))));
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private static List<string> Titles(string body, string prefix) =>
        [.. Heading().Matches(body).Select(m => m.Groups[1].Value).Where(t => t.StartsWith(prefix, StringComparison.Ordinal))];

    [GeneratedRegex("<h1[^>]*>(?:" + "zz-canary-" + ")?([^<]*)</h1>")]
    private static partial Regex Heading();

    private static string Pager(string body, string label) =>
        Regex.Match(body, "<nav class=\"qb-pager\" aria-label=\"" + label + "\">(.*?)</nav>", RegexOptions.Singleline).Groups[1].Value;

    [Fact]
    public async Task Every_item_appears_on_exactly_one_page_and_paging_ignores_the_limit()
    {
        var seen = new List<string>();
        foreach (var path in new[] { "/blog", "/blog?page=2", "/blog?page=3", "/blog?page=4" })
        {
            var (status, body) = await _world.VisitAsync(path);
            Assert.Equal(HttpStatusCode.OK, status);
            var titles = Titles(body, "Post ");
            Assert.Equal(10, titles.Count);
            seen.AddRange(titles);
        }

        Assert.Equal(Enumerable.Range(1, 40).Select(n => $"Post {n:00}"), seen);
    }

    [Fact]
    public async Task Two_lists_page_independently_and_their_links_keep_each_other()
    {
        var body = (await _world.VisitAsync("/blog?page=4&people=2")).Body;

        Assert.Equal(["Post 31", "Post 32", "Post 33", "Post 34", "Post 35", "Post 36", "Post 37", "Post 38", "Post 39", "Post 40"], Titles(body, "Post "));
        Assert.Equal(["Person 3", "Person 4"], Titles(body, "Person "));

        // The people pager keeps the posts' page 4, and the posts' pager keeps people's page 2.
        var people = Pager(body, "Pages");
        Assert.Contains("href=\"/blog?page=4&amp;people=3\" rel=\"next\"", people, StringComparison.Ordinal);
        Assert.Contains("href=\"/blog?page=4\" rel=\"prev\"", people, StringComparison.Ordinal);
        var posts = Pager(body, "Posts");
        Assert.Contains("href=\"/blog?page=3&amp;people=2\" rel=\"prev\"", posts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_current_page_is_marked_and_the_first_page_has_the_plain_address()
    {
        var posts = Pager((await _world.VisitAsync("/blog?page=2")).Body, "Posts");

        Assert.Contains("href=\"/blog?page=2\" aria-current=\"page\">2</a>", posts, StringComparison.Ordinal);
        Assert.Contains("href=\"/blog\" rel=\"prev\"", posts, StringComparison.Ordinal);
        Assert.Contains("href=\"/blog?page=3\" rel=\"next\"", posts, StringComparison.Ordinal);
        Assert.Contains("class=\"qb-pager__page\" href=\"/blog\">1</a>", posts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_past_the_last_shows_the_empty_state_and_the_way_back_with_status_200()
    {
        var (status, body) = await _world.VisitAsync("/blog?page=9");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Nothing yet", body, StringComparison.Ordinal);
        Assert.Empty(Titles(body, "Post "));
        Assert.Contains("href=\"/blog\">Back to the first page</a>", Pager(body, "Posts"), StringComparison.Ordinal);
        Assert.Equal(["Person 1", "Person 2"], Titles(body, "Person "));
    }

    [Fact]
    public async Task Parameters_that_change_nothing_share_the_first_pages_etag()
    {
        using var visitor = _world.Visitor();
        async Task<string> ETag(string path)
        {
            using var response = await visitor.GetAsync(new Uri(path, UriKind.Relative));
            return response.Headers.ETag!.Tag;
        }

        var plain = await ETag("/blog");

        Assert.Equal(plain, await ETag("/blog?page=1"));
        Assert.Equal(plain, await ETag("/blog?utm_source=mail"));
        Assert.Equal(plain, await ETag("/blog?page=abc&people=1"));
        Assert.Equal(plain, await ETag("/blog?page=99999"));
        Assert.NotEqual(plain, await ETag("/blog?page=2"));
        Assert.Equal(await ETag("/blog?page=2&people=3"), await ETag("/blog?people=3&page=2&x=y"));
    }
}
