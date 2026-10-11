using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Collection lists on published pages, end to end: the list's query, its cards bound per item,
/// item-page links, and a list in a component (016 US4, quickstart §4; tasks T061). The markup
/// itself is pinned by the unit goldens in <c>tests/fixtures/rendering/lists/</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CollectionListPageTests(DatabaseFixture database)
{
    private static async Task PostsAsync(TemplatesWorld world)
    {
        var day = 1;
        foreach (var (slug, featured) in new[] { ("one", true), ("two", true), ("three", false), ("four", true) })
        {
            await world.PublishedPostAsync(slug, new JsonObject
            {
                ["f1"] = $"{PagesApi.CanaryMarker}Post {slug}",
                ["f4"] = featured,
                ["f5"] = $"2026-01-0{day++}",
            });
        }
    }

    private static async Task TemplateAsync(TemplatesWorld world)
    {
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(post, world.Posts);
        await world.PublishDocumentAsync(post, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}""")));
    }

    /// <summary>The featured posts, newest first, three of them: quickstart §4 in small.</summary>
    private static string FeaturedList(Guid posts) => TemplatesWorld.Document(TemplatesWorld.ListOf(
        "latest",
        posts,
        """ "filters":[{"field":"f4","op":"eq","value":true}],"sort":[{"field":"f5","dir":"desc"}],"limit":2 """,
        TemplatesWorld.TitleAndLink()));

    [Fact]
    public async Task A_list_shows_the_matching_published_items_in_order_with_links_to_their_pages()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "list-page");
        await TemplateAsync(world);
        await PostsAsync(world);
        var home = await world.PageAsync("featured");
        await world.PublishDocumentAsync(home, FeaturedList(world.Posts));

        var (status, body) = await world.VisitAsync("/featured");

        Assert.Equal(HttpStatusCode.OK, status);
        var four = body.IndexOf("Post four</h1>", StringComparison.Ordinal);
        var two = body.IndexOf("Post two</h1>", StringComparison.Ordinal);
        Assert.True(four > 0 && two > four, "newest featured first");
        Assert.DoesNotContain("Post one</h1>", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Post three</h1>", body, StringComparison.Ordinal);
        Assert.Contains("href=\"/blog/four\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing yet", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_live_template_cards_show_but_link_nowhere_until_one_is_published()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "list-no-template");
        await PostsAsync(world);
        var home = await world.PageAsync("featured");
        await world.PublishDocumentAsync(home, FeaturedList(world.Posts));

        var before = (await world.VisitAsync("/featured")).Body;
        await TemplateAsync(world);
        var after = (await world.VisitAsync("/featured")).Body;

        Assert.Contains("Post four</h1>", before, StringComparison.Ordinal);
        Assert.DoesNotContain("/blog/", before, StringComparison.Ordinal);

        // Publishing the template refreshed the list page (research TB-10).
        Assert.Contains("href=\"/blog/four\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_list_with_nothing_to_show_shows_its_empty_state()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "list-empty");
        var home = await world.PageAsync("featured");
        await world.PublishDocumentAsync(home, FeaturedList(world.Posts));

        var (status, body) = await world.VisitAsync("/featured");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Nothing yet", body, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul class=\"qb-list\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_list_in_a_component_shows_the_same_items_on_every_page_that_shows_it()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "list-component");
        await TemplateAsync(world);
        await PostsAsync(world);

        var component = (await PagesApi.CreateComponentOkAsync(world.Admin, world.Site, "Featured posts")).GetProperty("id").GetGuid();
        var componentPath = PagesApi.ComponentPath(world.Site, component);
        await PublishingApi.SaveAsync(world.Admin, componentPath, FeaturedList(world.Posts));
        await PublishingApi.PublishCurrentAsync(world.Admin, componentPath);

        foreach (var slug in new[] { "first-page", "second-page" })
        {
            var page = await world.PageAsync(slug);
            await world.PublishDocumentAsync(page, PublishingApi.HeadingDocument(slug, component));
        }

        foreach (var path in new[] { "/first-page", "/second-page" })
        {
            var body = (await world.VisitAsync(path)).Body;
            Assert.Contains("Post four</h1>", body, StringComparison.Ordinal);
            Assert.Contains("href=\"/blog/two\"", body, StringComparison.Ordinal);
        }
    }
}
