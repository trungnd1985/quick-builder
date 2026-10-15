using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Rendering.Rendering.Navigation;
using Xunit;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// A menu shown by a published header, on the public site (024 US2, FR-004, FR-019, FR-031 to
/// FR-033, SC-002): drafts stay invisible, a menu publish refreshes exactly the pages that show
/// it, directly or through a component, item pages included, and pages with a menu admit
/// exactly the navigation script.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NavigationRenderTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_menu_publish_refreshes_exactly_the_pages_that_show_it()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "nav-render").ConfigureAwait(true);
        var services = await world.PageAsync("services").ConfigureAwait(true);
        await world.PublishDocumentAsync(services, PublishingApi.HeadingDocument("Services")).ConfigureAwait(true);

        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(
            Entry("services", PageTarget(services)),
            Entry("status", UrlTarget("https://status.example/"), label: "Status", newTab: true))).ConfigureAwait(true);
        await PublishMenuAsync(world, menu).ConfigureAwait(true);

        var header = await HeaderShowingAsync(world, menu).ConfigureAwait(true);
        var about = await world.PageAsync("about").ConfigureAwait(true);
        await world.PublishDocumentAsync(about, PublishingApi.HeadingDocument("About", header)).ConfigureAwait(true);
        var direct = await world.PageAsync("direct").ConfigureAwait(true);
        await world.PublishDocumentAsync(direct, PageShowing(menu)).ConfigureAwait(true);
        var plain = await world.PageAsync("plain").ConfigureAwait(true);
        await world.PublishDocumentAsync(plain, PublishingApi.HeadingDocument("Plain")).ConfigureAwait(true);

        // Item pages through a template that shows the header.
        var blog = await world.PageAsync("blog").ConfigureAwait(true);
        await world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(true);
        var template = await world.PageAsync("post", blog).ConfigureAwait(true);
        await world.TemplateOkAsync(template, world.Posts).ConfigureAwait(true);
        await world.PublishDocumentAsync(template, PublishingApi.HeadingDocument("Post", header)).ConfigureAwait(true);
        await world.PublishedPostAsync("hello").ConfigureAwait(true);

        var about1 = await GetAsync(world, "/about").ConfigureAwait(true);
        Assert.Contains("<nav class=\"qb-nav qb-nav--horizontal qb-nav--align-start\" aria-label=\"Main\"", about1.Body, StringComparison.Ordinal);
        Assert.Contains($"href=\"/services\">{PagesApi.Title("services")}</a>", about1.Body, StringComparison.Ordinal);
        Assert.Contains($"script-src 'sha256-{NavScript.Hash}'", about1.Csp, StringComparison.Ordinal);
        var direct1 = await GetAsync(world, "/direct").ConfigureAwait(true);
        var item1 = await GetAsync(world, "/blog/hello").ConfigureAwait(true);
        Assert.Contains("aria-label=\"Main\"", item1.Body, StringComparison.Ordinal);
        var plain1 = await GetAsync(world, "/plain").ConfigureAwait(true);
        Assert.DoesNotContain("script-src", plain1.Csp, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", plain1.Body, StringComparison.Ordinal);

        // A draft change reaches no page.
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("services", PageTarget(services), label: "What we do"))).ConfigureAwait(true);
        Assert.Equal(about1.ETag, (await GetAsync(world, "/about").ConfigureAwait(true)).ETag);

        // The publish reaches every page that shows it, and only those.
        await PublishMenuAsync(world, menu).ConfigureAwait(true);
        var about2 = await GetAsync(world, "/about").ConfigureAwait(true);
        Assert.NotEqual(about1.ETag, about2.ETag);
        Assert.Contains(">What we do</a>", about2.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Status", about2.Body, StringComparison.Ordinal);
        Assert.NotEqual(direct1.ETag, (await GetAsync(world, "/direct").ConfigureAwait(true)).ETag);
        Assert.NotEqual(item1.ETag, (await GetAsync(world, "/blog/hello").ConfigureAwait(true)).ETag);
        Assert.Equal(plain1.ETag, (await GetAsync(world, "/plain").ConfigureAwait(true)).ETag);
    }

    [Fact]
    public async Task A_renamed_menu_changes_no_page_until_it_is_published()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "nav-rename").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("a", UrlTarget("https://a.example/"), label: "A"))).ConfigureAwait(true);
        await PublishMenuAsync(world, menu).ConfigureAwait(true);
        var page = await world.PageAsync("home").ConfigureAwait(true);
        await world.PublishDocumentAsync(page, PageShowing(menu)).ConfigureAwait(true);

        var before = await GetAsync(world, "/home").ConfigureAwait(true);
        await PagesApi.OkAsync(await RenameAsync(world.Admin, world.Site, menu, "Primary",
            (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        var renamed = await GetAsync(world, "/home").ConfigureAwait(true);
        Assert.Equal(before.ETag, renamed.ETag);
        Assert.Contains("aria-label=\"Main\"", renamed.Body, StringComparison.Ordinal);

        await PublishMenuAsync(world, menu).ConfigureAwait(true);
        var published = await GetAsync(world, "/home").ConfigureAwait(true);
        Assert.NotEqual(before.ETag, published.ETag);
        Assert.Contains("aria-label=\"Primary\"", published.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_menu_publish_on_one_instance_is_served_by_the_other()
    {
        var connection = await database.CreateDatabaseAsync("nav-instances").ConfigureAwait(true);
        using var first = new QuickBuilderApp(connection);
        using var second = new QuickBuilderApp(connection);
        _ = first.Services;
        _ = second.Services;
        var admin = await first.CreateAdminClientAsync().ConfigureAwait(true);
        const string Host = "nav-instances.test";
        var site = await MediaApi.CreateSiteAsync(admin, "Instances", Host).ConfigureAwait(true);

        var menu = await CreateIdAsync(admin, site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(admin, site, menu, Document(Entry("a", UrlTarget("https://one.example/"), label: "First entry"))).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.PublishAsync(admin, MenuPath(site, menu), (await GetOkAsync(admin, site, menu).ConfigureAwait(true)).GetProperty("draftVersion").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        var page = (await PagesApi.CreateOkAsync(admin, site, "home").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PublishingApi.SaveAsync(admin, PagesApi.PagePath(site, page), PageShowing(menu)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(site, page)).ConfigureAwait(true);

        Assert.Contains("First entry", await VisitAsync(first, Host).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("First entry", await VisitAsync(second, Host).ConfigureAwait(true), StringComparison.Ordinal);

        await SaveCurrentDraftOkAsync(admin, site, menu, Document(Entry("a", UrlTarget("https://two.example/"), label: "Second entry"))).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.PublishAsync(admin, MenuPath(site, menu), (await GetOkAsync(admin, site, menu).ConfigureAwait(true)).GetProperty("draftVersion").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Contains("Second entry", await VisitAsync(second, Host).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("Second entry", await VisitAsync(first, Host).ConfigureAwait(true), StringComparison.Ordinal);
    }

    internal static async Task PublishMenuAsync(TemplatesWorld world, Guid menu)
    {
        var draftVersion = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(false)).GetProperty("draftVersion").GetUInt32();
        await PagesApi.OkAsync(await PublishingApi.PublishAsync(world.Admin, MenuPath(world.Site, menu), draftVersion).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>A live header component that shows <paramref name="menu"/>.</summary>
    internal static async Task<Guid> HeaderShowingAsync(TemplatesWorld world, Guid menu)
    {
        var header = (await PagesApi.CreateComponentOkAsync(world.Admin, world.Site, $"Header {menu.ToString("N")[..6]}").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var path = PagesApi.ComponentPath(world.Site, header);
        await PublishingApi.SaveAsync(world.Admin, path, PageShowing(menu)).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(world.Admin, path).ConfigureAwait(false);
        return header;
    }

    internal sealed record Visit(string Body, string? ETag, string Csp);

    internal static async Task<Visit> GetAsync(TemplatesWorld world, string path)
    {
        using var visitor = world.Visitor();
        using var response = await visitor.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new Visit(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false),
            response.Headers.ETag?.Tag,
            response.Headers.TryGetValues("Content-Security-Policy", out var csp) ? string.Join(';', csp) : string.Empty);
    }

    private static async Task<string> VisitAsync(QuickBuilderApp app, string host)
    {
        using var visitor = app.CreateClientForHost(host);
        return await visitor.GetStringAsync(new Uri("/home", UriKind.Relative)).ConfigureAwait(false);
    }
}
