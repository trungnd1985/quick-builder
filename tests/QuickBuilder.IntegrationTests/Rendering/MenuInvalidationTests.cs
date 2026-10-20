using System.Text.Json;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// The rows of 024 research NV-07 after the menu publish (tasks T057, SC-002): a change to what
/// a live menu points at refreshes exactly the pages that show the menu, directly or through a
/// component, item pages under a template that shows it included, and no other page.
/// </summary>
/// <remarks>
/// The world: a live menu of <c>/services</c> (no label), <c>/contact</c> and the post
/// <c>hello</c>; a header component showing it on <c>/about</c> and on the post template; the
/// menu directly on <c>/direct</c>; and <c>/plain</c>, which shows no menu. Generations say
/// which pages a change refreshes. A menu publish across two instances is
/// <see cref="NavigationRenderTests.A_menu_publish_on_one_instance_is_served_by_the_other"/>:
/// every refresh here moves the same generation rows, which both instances read.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MenuInvalidationTests(DatabaseFixture database)
{
    private sealed record World(TemplatesWorld Templates, Guid Services, Guid Contact, Guid Blog, Guid Template, Guid Hello, Guid About, Guid Direct, Guid Plain)
        : IDisposable
    {
        public void Dispose() => Templates.Dispose();

        public async Task<Dictionary<Guid, long?>> GenerationsAsync()
        {
            var result = new Dictionary<Guid, long?>();
            foreach (var page in new[] { About, Direct, Template, Plain })
            {
                result[page] = await Templates.GenerationAsync(page).ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>The pages that show the menu: through the header, directly, and the item pages.</summary>
        public Guid[] Showing => [About, Direct, Template];
    }

    private async Task<World> WorldAsync(string label)
    {
        var world = await TemplatesWorld.CreateAsync(database, label).ConfigureAwait(false);
        var services = await world.PageAsync("services").ConfigureAwait(false);
        await world.PublishDocumentAsync(services, PublishingApi.HeadingDocument("Services")).ConfigureAwait(false);
        var contact = await world.PageAsync("contact").ConfigureAwait(false);
        await world.PublishDocumentAsync(contact, PublishingApi.HeadingDocument("Contact")).ConfigureAwait(false);
        var hello = ItemsApi.Id(await world.PublishedPostAsync("hello").ConfigureAwait(false));

        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(false);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(
            Entry("services", PageTarget(services)),
            Entry("contact", PageTarget(contact), label: "Contact us"),
            Entry("hello", ItemTarget(world.Posts, hello), label: "Hello"))).ConfigureAwait(false);
        await NavigationRenderTests.PublishMenuAsync(world, menu).ConfigureAwait(false);
        var header = await NavigationRenderTests.HeaderShowingAsync(world, menu).ConfigureAwait(false);

        var blog = await world.PageAsync("blog").ConfigureAwait(false);
        await world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(false);
        var template = await world.PageAsync("post", blog).ConfigureAwait(false);
        await world.TemplateOkAsync(template, world.Posts).ConfigureAwait(false);
        await world.PublishDocumentAsync(template, PublishingApi.HeadingDocument("Post", header)).ConfigureAwait(false);

        var about = await world.PageAsync("about").ConfigureAwait(false);
        await world.PublishDocumentAsync(about, PublishingApi.HeadingDocument("About", header)).ConfigureAwait(false);
        var direct = await world.PageAsync("direct").ConfigureAwait(false);
        await world.PublishDocumentAsync(direct, PageShowing(menu)).ConfigureAwait(false);
        var plain = await world.PageAsync("plain").ConfigureAwait(false);
        await world.PublishDocumentAsync(plain, PublishingApi.HeadingDocument("Plain")).ConfigureAwait(false);

        return new World(world, services, contact, blog, template, hello, about, direct, plain);
    }

    [Fact]
    public async Task Changes_to_a_target_page_refresh_exactly_the_pages_showing_the_menu()
    {
        using var w = await WorldAsync("menu-inv-pages").ConfigureAwait(true);
        var t = w.Templates;

        // Retitled at publish: the entry has no label of its own, so it shows the live title.
        var before = await w.GenerationsAsync().ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(t.Admin, t.Site, w.Services, "What we do", "services", await VersionAsync(t, w.Services).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(t.Admin, PagesApi.PagePath(t.Site, w.Services)).ConfigureAwait(true);
        var retitled = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(before, retitled, w.Showing);
        Assert.Contains(">What we do</a>", (await NavigationRenderTests.GetAsync(t, "/about").ConfigureAwait(true)).Body, StringComparison.Ordinal);

        // Slug changed.
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(t.Admin, t.Site, w.Services, "What we do", "offer", await VersionAsync(t, w.Services).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        var slugged = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(retitled, slugged, w.Showing);
        Assert.Contains("href=\"/offer\"", (await NavigationRenderTests.GetAsync(t, "/direct").ConfigureAwait(true)).Body, StringComparison.Ordinal);

        // Moved under another page.
        await PagesApi.OkAsync(await PagesApi.MoveAsync(t.Admin, t.Site, w.Services, w.Contact, null, await VersionAsync(t, w.Services).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        var moved = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(slugged, moved, w.Showing);
        Assert.Contains("href=\"/contact/offer\"", (await NavigationRenderTests.GetAsync(t, "/direct").ConfigureAwait(true)).Body, StringComparison.Ordinal);

        // Unpublished: its link leaves the menu.
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(t.Admin, PagesApi.PagePath(t.Site, w.Contact)).ConfigureAwait(true)).ConfigureAwait(true);
        var unpublished = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(moved, unpublished, w.Showing);
        Assert.DoesNotContain("Contact us", (await NavigationRenderTests.GetAsync(t, "/direct").ConfigureAwait(true)).Body, StringComparison.Ordinal);

        // Made home: its address becomes the site root.
        await PagesApi.OkAsync(await PagesApi.MoveAsync(t.Admin, t.Site, w.Services, null, null, await VersionAsync(t, w.Services).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        var back = await w.GenerationsAsync().ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.MakeHomeAsync(t.Admin, t.Site, w.Services, await VersionAsync(t, w.Services).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        var home = await w.GenerationsAsync().ConfigureAwait(true);
        foreach (var page in w.Showing)
        {
            Assert.True(home[page] > back[page], $"{page} should refresh when its menu's target becomes the home page");
        }
    }

    [Fact]
    public async Task Changes_to_a_target_item_refresh_exactly_the_pages_showing_the_menu()
    {
        using var w = await WorldAsync("menu-inv-items").ConfigureAwait(true);
        var t = w.Templates;

        var before = await w.GenerationsAsync().ConfigureAwait(true);
        await t.App.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, w.Hello, 2, "manual", true, null)).ConfigureAwait(true);
        var published = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(before, published, w.Showing);

        await t.App.DeliverAsync(new ItemUnpublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, w.Hello, false, null)).ConfigureAwait(true);
        var unpublished = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(published, unpublished, w.Showing);

        // A draft-only item was never in a live menu; a published one was.
        await t.App.DeliverAsync(new ItemDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, w.Hello, false, null)).ConfigureAwait(true);
        AssertMoved(unpublished, await w.GenerationsAsync().ConfigureAwait(true));
        await t.App.DeliverAsync(new ItemDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, w.Hello, true, null)).ConfigureAwait(true);
        AssertMoved(unpublished, await w.GenerationsAsync().ConfigureAwait(true), w.Showing);

        // An item no live menu points at refreshes no menu page.
        var quiet = await w.GenerationsAsync().ConfigureAwait(true);
        await t.App.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, t.Site, t.Posts, Guid.NewGuid(), 1, "manual", false, null)).ConfigureAwait(true);
        AssertMoved(quiet, await w.GenerationsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Changes_to_the_item_template_refresh_the_pages_showing_the_menu()
    {
        using var w = await WorldAsync("menu-inv-template").ConfigureAwait(true);
        var t = w.Templates;
        var others = new[] { w.About, w.Direct };

        // Published again: the template's own row moves with its publish; the others through the menu.
        var before = await w.GenerationsAsync().ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(t.Admin, PagesApi.PagePath(t.Site, w.Template)).ConfigureAwait(true);
        var published = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(before, published, [.. others, w.Template]);

        // Moved under another page: the item's address changes.
        await PagesApi.OkAsync(await PagesApi.MoveAsync(t.Admin, t.Site, w.Template, w.Contact, null, await VersionAsync(t, w.Template).ConfigureAwait(true)).ConfigureAwait(true)).ConfigureAwait(true);
        var moved = await w.GenerationsAsync().ConfigureAwait(true);
        AssertMoved(published, moved, [.. others, w.Template]);
        Assert.Contains("href=\"/contact/hello\"", (await NavigationRenderTests.GetAsync(t, "/direct").ConfigureAwait(true)).Body, StringComparison.Ordinal);

        // Unpublished: the item has no page any more, so its entry leaves the menu.
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(t.Admin, PagesApi.PagePath(t.Site, w.Template)).ConfigureAwait(true)).ConfigureAwait(true);
        var unpublished = await w.GenerationsAsync().ConfigureAwait(true);
        foreach (var page in others)
        {
            Assert.True(unpublished[page] > moved[page], $"{page} should refresh when the item template is unpublished");
        }

        Assert.Equal(moved[w.Plain], unpublished[w.Plain]);
        Assert.DoesNotContain(">Hello</a>", (await NavigationRenderTests.GetAsync(t, "/direct").ConfigureAwait(true)).Body, StringComparison.Ordinal);
    }

    private static async Task<int> VersionAsync(TemplatesWorld world, Guid pageId)
    {
        var page = await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, pageId).ConfigureAwait(false)).ConfigureAwait(false);
        return page.GetProperty("version").GetInt32();
    }

    /// <summary>Exactly <paramref name="expected"/> moved on; every other page kept its generation.</summary>
    private static void AssertMoved(Dictionary<Guid, long?> before, Dictionary<Guid, long?> after, params Guid[] expected)
    {
        foreach (var (page, generation) in before)
        {
            if (expected.Contains(page))
            {
                Assert.True(after[page] > generation, $"{page} should have refreshed");
            }
            else
            {
                Assert.Equal(generation, after[page]);
            }
        }
    }
}
