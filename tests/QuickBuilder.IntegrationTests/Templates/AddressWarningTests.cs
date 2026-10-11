
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>
/// Changes that move, end or hide item addresses say so in their response; none is refused
/// (016 research TB-12, data-model §4; tasks T084).
/// </summary>
/// <remarks>Two published posts, <c>hello</c> and <c>archive</c>, and a template at <c>/blog/{slug}</c>.</remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AddressWarningTests(DatabaseFixture database)
{
    private static async Task<(TemplatesWorld World, Guid Blog, Guid Template)> WorldAsync(DatabaseFixture database, string label)
    {
        var world = await TemplatesWorld.CreateAsync(database, label);
        await world.PublishedPostAsync("hello");
        await world.PublishedPostAsync("archive");
        var blog = await world.PageAsync("blog");
        var template = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(template, world.Posts);
        await world.PublishDocumentAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}""")));
        return (world, blog, template);
    }

    private static List<(string Code, int? Count, Guid? ItemId)> Warnings(JsonElement response) =>
        [.. response.GetProperty("warnings").EnumerateArray().Select(w => (
            w.GetProperty("code").GetString()!,
            w.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : (int?)null,
            w.TryGetProperty("itemId", out var i) && i.ValueKind == JsonValueKind.String ? i.GetGuid() : (Guid?)null))];

    private static async Task<int> VersionAsync(TemplatesWorld world, Guid pageId) =>
        (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, pageId))).GetProperty("version").GetInt32();

    [Fact]
    public async Task Moving_a_template_or_renaming_its_parent_says_how_many_items_change_address()
    {
        var (world, blog, template) = await WorldAsync(database, "warn-move");
        using var _ = world;
        var articles = await world.PageAsync("articles");

        var moved = await PagesApi.OkAsync(await PagesApi.MoveAsync(world.Admin, world.Site, template, articles, null, await VersionAsync(world, template)));
        Assert.Equal([("items_change_address", (int?)2, (Guid?)null)], Warnings(moved));

        var renamed = await PagesApi.OkAsync(await PagesApi.UpdateAsync(world.Admin, world.Site, articles, "Articles", "stories", await VersionAsync(world, articles)));
        Assert.Equal([("items_change_address", (int?)2, (Guid?)null)], Warnings(renamed));

        // A rename that keeps the address moves nothing.
        var retitled = await PagesApi.OkAsync(await PagesApi.UpdateAsync(world.Admin, world.Site, blog, "The blog", "blog", await VersionAsync(world, blog)));
        Assert.Empty(Warnings(retitled));
    }

    [Fact]
    public async Task Unpublishing_a_live_template_says_how_many_items_lose_their_address()
    {
        var (world, _, template) = await WorldAsync(database, "warn-unpublish");
        using var _w = world;

        var unpublished = await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, template)));

        Assert.Equal([("items_lose_address", (int?)2, (Guid?)null)], Warnings(unpublished));
    }

    [Fact]
    public async Task Turning_a_live_template_back_says_how_many_items_lose_their_address()
    {
        var (world, _, template) = await WorldAsync(database, "warn-remove");
        using var _w = world;

        var removed = await PagesApi.OkAsync(await world.Admin.DeleteAsync(new Uri($"{world.TemplatePath(template)}?version={await VersionAsync(world, template)}", UriKind.Relative)));

        Assert.Equal([("items_lose_address", (int?)2, (Guid?)null)], Warnings(removed));
    }

    [Fact]
    public async Task A_page_beside_a_template_at_an_items_slug_says_it_hides_that_items_page()
    {
        var (world, blog, _) = await WorldAsync(database, "warn-hidden");
        using var _w = world;
        var archive = ItemsArchiveId(await Cms.Items.ItemsApi.ListOkAsync(world.Admin, world.Site, world.Posts));

        var created = await PagesApi.OkAsync(await PagesApi.CreateAsync(world.Admin, world.Site, "archive", blog), System.Net.HttpStatusCode.Created);
        Assert.Equal([("item_address_hidden", (int?)null, (Guid?)archive)], Warnings(created));

        var plain = await PagesApi.OkAsync(await PagesApi.CreateAsync(world.Admin, world.Site, "about", blog), System.Net.HttpStatusCode.Created);
        Assert.Empty(Warnings(plain));
    }

    private static Guid ItemsArchiveId(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().First(i => i.GetProperty("slug").GetString() == "archive").GetProperty("id").GetGuid();
}
