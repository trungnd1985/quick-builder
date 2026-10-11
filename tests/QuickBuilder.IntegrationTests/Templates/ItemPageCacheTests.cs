using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Rendering.Domain;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>
/// Item pages are cached per item and refreshed when that item changes (016 US1, US3, FR-031;
/// research TB-10; tasks T028).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemPageCacheTests(DatabaseFixture database)
{
    private static async Task TemplateAsync(TemplatesWorld world)
    {
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(post, world.Posts);
        await world.PublishDocumentAsync(post, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}""")));
    }

    private static long Renders(TemplatesWorld world) => world.App.Services.GetRequiredService<RenderMetrics>().Renders;

    [Fact]
    public async Task A_second_request_is_served_from_the_cache_and_a_matching_etag_gets_304()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "item-cache-hit");
        await TemplateAsync(world);
        await world.PublishedPostAsync("hello-world");
        using var visitor = world.Visitor();

        using var first = await visitor.GetAsync(new Uri("/blog/hello-world", UriKind.Relative));
        var rendered = Renders(world);
        using var second = await visitor.GetAsync(new Uri("/blog/hello-world", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(rendered, Renders(world));
        Assert.Equal(first.Headers.ETag, second.Headers.ETag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/blog/hello-world");
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var notModified = await visitor.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task Publishing_an_item_refreshes_its_page_and_leaves_other_items_pages_cached()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "item-cache-publish");
        await TemplateAsync(world);
        var hello = await world.PublishedPostAsync("hello-world", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Before" });
        await world.PublishedPostAsync("other");
        using var visitor = world.Visitor();

        using var helloBefore = await visitor.GetAsync(new Uri("/blog/hello-world", UriKind.Relative));
        using var otherBefore = await visitor.GetAsync(new Uri("/blog/other", UriKind.Relative));

        var saved = await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(hello),
            ItemsApi.Draft("hello-world", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}After" }, world.Items.PostsSchemaRevision, ItemsApi.Revision(hello)));
        await ItemsApi.PublishOkAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(saved), ItemsApi.Revision(saved));

        var rendered = Renders(world);
        using var helloAfter = await visitor.GetAsync(new Uri("/blog/hello-world", UriKind.Relative));
        using var otherAfter = await visitor.GetAsync(new Uri("/blog/other", UriKind.Relative));

        Assert.NotEqual(helloBefore.Headers.ETag, helloAfter.Headers.ETag);
        Assert.Contains("After</h1>", await helloAfter.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(otherBefore.Headers.ETag, otherAfter.Headers.ETag);
        Assert.Equal(rendered + 1, Renders(world));
    }

    [Fact]
    public async Task Saving_an_items_draft_changes_nothing_visitors_see()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "item-cache-draft");
        await TemplateAsync(world);
        var hello = await world.PublishedPostAsync("hello-world", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Live" });
        using var visitor = world.Visitor();
        using var before = await visitor.GetAsync(new Uri("/blog/hello-world", UriKind.Relative));

        await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(hello),
            ItemsApi.Draft("hello-world", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Draft" }, world.Items.PostsSchemaRevision, ItemsApi.Revision(hello)));
        using var after = await visitor.GetAsync(new Uri("/blog/hello-world", UriKind.Relative));

        Assert.Equal(before.Headers.ETag, after.Headers.ETag);
        Assert.Contains("Live</h1>", await after.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
