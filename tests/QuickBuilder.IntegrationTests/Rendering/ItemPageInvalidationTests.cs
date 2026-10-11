using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Exactly the pages a change affects refresh, and every other page stays cached (016 US3,
/// research TB-10; tasks T050).
/// </summary>
/// <remarks>
/// The world: a <c>/blog/{slug}</c> template of posts and three published posts; live pages
/// <c>/news</c> (a list of posts, which reference people), <c>/team</c> (a list of people) and
/// <c>/about</c> (no list). Generations say which pages a change refreshes; visits say that a
/// refreshed page renders anew and every other page is served from the cache (T066a).
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ItemPageInvalidationTests(DatabaseFixture database)
{
    private sealed record World(TemplatesWorld Templates, Guid Blog, Guid Template, Guid News, Guid Team, Guid About, List<Guid> Posts) : IDisposable
    {
        public void Dispose() => Templates.Dispose();

        public async Task<Dictionary<Guid, long?>> GenerationsAsync()
        {
            var result = new Dictionary<Guid, long?>();
            foreach (var page in new[] { Template, News, Team, About })
            {
                result[page] = await Templates.GenerationAsync(page).ConfigureAwait(false);
            }

            return result;
        }
    }

    private async Task<World> WorldAsync(string label)
    {
        var world = await TemplatesWorld.CreateAsync(database, label);
        var blog = await world.PageAsync("blog");
        var template = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(template, world.Posts);
        await world.PublishDocumentAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}""")));

        var posts = new List<Guid>();
        foreach (var slug in new[] { "first", "second", "third" })
        {
            posts.Add(ItemsApi.Id(await world.PublishedPostAsync(slug)));
        }

        var news = await world.PageAsync("news");
        await world.PublishDocumentAsync(news, TemplatesWorld.Document(TemplatesWorld.ListOf("l1", world.Posts, null, TemplatesWorld.TitleAndLink())));
        var team = await world.PageAsync("team");
        await world.PublishDocumentAsync(team, TemplatesWorld.Document(TemplatesWorld.ListOf("l1", world.People)));
        var about = await world.PageAsync("about");
        await world.PublishDocumentAsync(about, PublishingApi.HeadingDocument("About"));

        return new World(world, blog, template, news, team, about, posts);
    }

    private static long Renders(TemplatesWorld world) => world.App.Services.GetRequiredService<RenderMetrics>().Renders;

    /// <summary>Warms every item page; returns the render count after.</summary>
    private static async Task<long> WarmItemPagesAsync(TemplatesWorld world, params string[] slugs)
    {
        foreach (var slug in slugs)
        {
            Assert.Equal(HttpStatusCode.OK, (await world.VisitAsync($"/blog/{slug}")).Status);
        }

        return Renders(world);
    }

    private static void AssertMoved(Dictionary<Guid, long?> before, Dictionary<Guid, long?> after, params Guid[] moved)
    {
        foreach (var (page, generation) in before)
        {
            if (moved.Contains(page))
            {
                Assert.True(after[page] > generation, $"page {page} should have refreshed");
            }
            else
            {
                Assert.Equal(generation, after[page]);
            }
        }
    }

    [Fact]
    public async Task Publishing_an_item_refreshes_its_page_and_the_lists_of_its_collection_only()
    {
        using var world = await WorldAsync("invalidate-item-publish");
        var t = world.Templates;
        await WarmItemPagesAsync(t, "first", "second", "third");
        var before = await world.GenerationsAsync();

        var first = await ItemsApi.GetOkAsync(t.Admin, t.Site, t.Posts, world.Posts[0]);
        var saved = await ItemsApi.SaveOkAsync(t.Admin, t.Site, t.Posts, world.Posts[0],
            ItemsApi.Draft("first", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Changed" }, t.Items.PostsSchemaRevision, ItemsApi.Revision(first)));
        await ItemsApi.PublishOkAsync(t.Admin, t.Site, t.Posts, world.Posts[0], ItemsApi.Revision(saved));
        await t.App.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, world.Posts[0], 2, "manual", false, null));

        AssertMoved(before, await world.GenerationsAsync(), world.News);

        var rendered = Renders(t);
        Assert.Contains("Changed</h1>", (await t.VisitAsync("/blog/first")).Body, StringComparison.Ordinal);
        await WarmItemPagesAsync(t, "second", "third");
        Assert.Equal(rendered + 1, Renders(t));
    }

    [Fact]
    public async Task A_refreshed_list_page_renders_anew_with_the_new_item_and_the_others_stay_cached()
    {
        using var world = await WorldAsync("invalidate-list-render");
        var t = world.Templates;
        foreach (var path in new[] { "/news", "/team", "/about" })
        {
            Assert.Equal(HttpStatusCode.OK, (await t.VisitAsync(path)).Status);
        }

        Assert.DoesNotContain("Fourth</h1>", (await t.VisitAsync("/news")).Body, StringComparison.Ordinal);
        var fourth = await t.PublishedPostAsync("fourth", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Fourth" });
        await t.App.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, ItemsApi.Id(fourth), 1, "manual", false, null));

        var rendered = Renders(t);
        var news = (await t.VisitAsync("/news")).Body;
        await t.VisitAsync("/team");
        await t.VisitAsync("/about");

        Assert.Contains("Fourth</h1>", news, StringComparison.Ordinal);
        Assert.Contains("href=\"/blog/fourth\"", news, StringComparison.Ordinal);
        Assert.Equal(rendered + 1, Renders(t));
    }

    [Fact]
    public async Task Unpublishing_or_deleting_an_item_refreshes_the_lists_of_its_collection_only()
    {
        using var world = await WorldAsync("invalidate-item-remove");
        var t = world.Templates;
        var before = await world.GenerationsAsync();

        await t.App.DeliverAsync(new ItemUnpublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, world.Posts[0], false, null));
        var afterUnpublish = await world.GenerationsAsync();
        AssertMoved(before, afterUnpublish, world.News);

        // A draft-only item was never listed.
        await t.App.DeliverAsync(new ItemDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, Guid.NewGuid(), false, null));
        AssertMoved(afterUnpublish, await world.GenerationsAsync());

        await t.App.DeliverAsync(new ItemDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, world.Posts[1], true, null));
        AssertMoved(afterUnpublish, await world.GenerationsAsync(), world.News);
    }

    [Fact]
    public async Task Publishing_a_referenced_item_refreshes_the_lists_of_collections_referencing_it()
    {
        using var world = await WorldAsync("invalidate-referenced");
        var t = world.Templates;
        var before = await world.GenerationsAsync();

        await t.App.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.People, Guid.NewGuid(), 1, "manual", false, null));

        // Posts reference people, so a card of /news may show an author's name.
        AssertMoved(before, await world.GenerationsAsync(), world.News, world.Team);
    }

    [Fact]
    public async Task A_redelivered_event_refreshes_once()
    {
        using var world = await WorldAsync("invalidate-redelivered");
        var t = world.Templates;
        var before = await t.GenerationAsync(world.News);
        var published = new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, world.Posts[0], 1, "manual", false, null);

        await t.App.DeliverAsync(published);
        await t.App.DeliverAsync(published);

        Assert.Equal(before + 1, await t.GenerationAsync(world.News));
    }

    [Fact]
    public async Task A_schema_change_refreshes_every_page_showing_the_collection_and_a_rename_refreshes_nothing()
    {
        using var world = await WorldAsync("invalidate-schema");
        var t = world.Templates;
        await WarmItemPagesAsync(t, "first", "second");
        var before = await world.GenerationsAsync();

        await t.App.DeliverAsync(new CollectionChanged(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, 3, false, false, null));
        AssertMoved(before, await world.GenerationsAsync());

        await t.App.DeliverAsync(new CollectionChanged(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, 4, true, false, null));
        var afterSchema = await world.GenerationsAsync();
        AssertMoved(before, afterSchema, world.News, world.Template);

        // Every item page is keyed by its template's generation, so each renders anew.
        var rendered = Renders(t);
        await WarmItemPagesAsync(t, "first", "second");
        Assert.Equal(rendered + 2, Renders(t));

        // A collection referenced by posts: rewritten people values may show on post pages.
        await t.App.DeliverAsync(new CollectionChanged(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.People, 2, false, true, null));
        AssertMoved(afterSchema, await world.GenerationsAsync(), world.News, world.Team, world.Template);
    }

    [Fact]
    public async Task Publishing_or_moving_the_template_refreshes_the_lists_linking_to_its_items()
    {
        using var world = await WorldAsync("invalidate-template");
        var t = world.Templates;
        var before = await world.GenerationsAsync();

        await PublishingApi.PublishCurrentAsync(t.Admin, PagesApi.PagePath(t.Site, world.Template));
        var afterPublish = await world.GenerationsAsync();
        AssertMoved(before, afterPublish, world.News, world.Template);

        var articles = await t.PageAsync("articles");
        var node = await PagesApi.OkAsync(await PagesApi.GetAsync(t.Admin, t.Site, world.Template));
        await PagesApi.OkAsync(await PagesApi.MoveAsync(t.Admin, t.Site, world.Template, articles, null, node.GetProperty("version").GetInt32()));

        AssertMoved(afterPublish, await world.GenerationsAsync(), world.News, world.Template);
        Assert.Equal(HttpStatusCode.OK, (await t.VisitAsync("/articles/first")).Status);
    }

    [Fact]
    public async Task A_template_publish_records_its_collection_and_a_list_over_a_multi_reference_field_its_target()
    {
        using var world = await WorldAsync("invalidate-dependencies");
        var t = world.Templates;

        await t.PublishDocumentAsync(world.Template, TemplatesWorld.Document(
            TemplatesWorld.BoundHeading("title", """{"field":"f1"}"""),
            TemplatesWorld.List("related", """{"field":"f10"}""")));

        var rows = await t.LiveDependenciesAsync(world.Template);
        Assert.Contains(("template", t.Posts), rows);
        Assert.Contains(("collection", t.People), rows);
        Assert.Contains(("collection", t.Posts), await t.LiveDependenciesAsync(world.News));
    }

    [Fact]
    public async Task A_media_change_refreshes_the_lists_of_collections_whose_published_items_show_the_file()
    {
        using var world = await WorldAsync("invalidate-media");
        var t = world.Templates;
        await t.PublishedPostAsync("with-cover", new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Cover",
            ["f6"] = new JsonObject { ["mediaId"] = t.Items.Image.ToString() },
        });
        var before = await world.GenerationsAsync();

        await t.App.DeliverAsync(new FileChanged(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Items.Image, FileChangeReasons.Replaced));

        AssertMoved(before, await world.GenerationsAsync(), world.News);
    }
}
