using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// <c>pages.collection_uses</c> follows every change to what a page or component uses, in each
/// state, with the field ids it reads (016 data-model §1.3, research TB-13; tasks T081).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CollectionUsesTests(DatabaseFixture database)
{
    private static string ListOfPosts(TemplatesWorld world, string sortField = "f5") => TemplatesWorld.Document(TemplatesWorld.ListOf(
        "l1", world.Posts, $$""" "sort":[{"field":"{{sortField}}","dir":"desc"}] """, TemplatesWorld.BoundHeading("t", """{"field":"f1"}""")));

    [Fact]
    public async Task Draft_save_publish_unpublish_and_copy_to_draft_keep_the_rows_of_each_state()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "uses-states");
        var page = await world.PageAsync("news");
        var path = PagesApi.PagePath(world.Site, page);

        await PublishingApi.SaveAsync(world.Admin, path, ListOfPosts(world));
        Assert.Equal([$"draft:{world.Posts}:f1,f5"], await world.CollectionUsesAsync(page));

        await PublishingApi.PublishCurrentAsync(world.Admin, path);
        Assert.Equal([$"draft:{world.Posts}:f1,f5", $"live:{world.Posts}:f1,f5"], await world.CollectionUsesAsync(page));

        // The draft moves on; the live version still uses what it used.
        await PublishingApi.SaveAsync(world.Admin, path, ListOfPosts(world, sortField: "f3"));
        Assert.Equal([$"draft:{world.Posts}:f1,f3", $"live:{world.Posts}:f1,f5"], await world.CollectionUsesAsync(page));

        // Copying version 1 back makes the draft use what it used.
        var draftVersion = await PublishingApi.DraftVersionAsync(world.Admin, path);
        Assert.Equal(HttpStatusCode.OK, (await PublishingApi.CopyToDraftAsync(world.Admin, path, 1, draftVersion)).StatusCode);
        Assert.Equal([$"draft:{world.Posts}:f1,f5", $"live:{world.Posts}:f1,f5"], await world.CollectionUsesAsync(page));

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, path));
        Assert.Equal([$"draft:{world.Posts}:f1,f5"], await world.CollectionUsesAsync(page));

        // A draft without lists or bindings uses nothing.
        await PublishingApi.SaveAsync(world.Admin, path, PublishingApi.HeadingDocument("Plain"));
        Assert.Empty(await world.CollectionUsesAsync(page));
    }

    [Fact]
    public async Task A_scheduled_publish_uses_its_captured_content_until_cancelled()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "uses-scheduled");
        var page = await world.PageAsync("news");
        var path = PagesApi.PagePath(world.Site, page);
        await PublishingApi.SaveAsync(world.Admin, path, ListOfPosts(world));

        var draftVersion = await PublishingApi.DraftVersionAsync(world.Admin, path);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(world.Admin, path, "publish", DateTimeOffset.UtcNow.AddDays(1), draftVersion));
        Assert.Contains($"scheduled:{world.Posts}:f1,f5", await world.CollectionUsesAsync(page));

        Assert.Equal(HttpStatusCode.NoContent, (await PublishingApi.CancelScheduleAsync(world.Admin, path, "publish")).StatusCode);
        Assert.Equal([$"draft:{world.Posts}:f1,f5"], await world.CollectionUsesAsync(page));
    }

    [Fact]
    public async Task A_template_uses_its_collection_in_every_state_until_it_is_turned_back_and_deleting_forgets_all()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "uses-template");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(post, world.Posts);
        Assert.Equal([$"draft:{world.Posts}:"], await world.CollectionUsesAsync(post));

        await world.PublishDocumentAsync(post, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}""")));
        Assert.Equal([$"draft:{world.Posts}:f1", $"live:{world.Posts}:f1"], await world.CollectionUsesAsync(post));

        // Turned back into a page, its bindings read no scope any more.
        var version = (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, post))).GetProperty("version").GetInt32();
        await PagesApi.OkAsync(await world.Admin.DeleteAsync(new Uri($"{world.TemplatePath(post)}?version={version}", UriKind.Relative)));
        Assert.Empty(await world.CollectionUsesAsync(post));

        var list = await world.PageAsync("news");
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, list), ListOfPosts(world));
        var listVersion = (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, list))).GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.DeleteAsync(world.Admin, world.Site, list, listVersion)).StatusCode);
        Assert.Empty(await world.CollectionUsesAsync(list));
    }

    [Fact]
    public async Task A_component_uses_what_its_lists_read()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "uses-component");
        var component = (await PagesApi.CreateComponentOkAsync(world.Admin, world.Site, "Latest")).GetProperty("id").GetGuid();

        await PublishingApi.SaveAsync(world.Admin, PagesApi.ComponentPath(world.Site, component), ListOfPosts(world));

        Assert.Equal([$"draft:{world.Posts}:f1,f5"], await world.CollectionUsesAsync(component));
    }
}
