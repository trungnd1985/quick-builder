using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Rendering.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 7: each published page carries one JSON-LD graph, and breadcrumbs follow their
/// ancestors' live titles at the next request (017 FR-034 to FR-036; research SE-18).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class StructuredDataRenderTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _home;
    private Guid _blog;
    private Guid _about;
    private Guid _team;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "seo-jsonld");
        _home = await _world.PageAsync("home");
        await PublishTitledAsync(_home, "Home");
        await PagesApi.OkAsync(await PagesApi.MakeHomeAsync(_world.Admin, _world.Site, _home, await VersionAsync(_home)));
        _blog = await _world.PageAsync("blog");
        await PublishTitledAsync(_blog, "Blog");
        var template = await _world.PageAsync("post", _blog);
        await _world.TemplateOkAsync(template, _world.Posts);
        await _world.PublishDocumentAsync(template, SeoApi.DocumentWithSeo("Post", """{ "structuredData": { "type": "Article" } }"""));
        await _world.PublishedPostAsync("hello", new JsonObject { ["f1"] = PagesApi.Title("hello") });
        _about = await _world.PageAsync("about");
        await PublishTitledAsync(_about, "About");
        _team = await _world.PageAsync("team", _about);
        await PublishTitledAsync(_team, "Team");
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private async Task<int> VersionAsync(Guid page) =>
        (await PagesApi.OkAsync(await PagesApi.GetAsync(_world.Admin, _world.Site, page))).GetProperty("version").GetInt32();

    /// <summary>Publishes a heading document whose live title is <paramref name="title"/>.</summary>
    private async Task PublishTitledAsync(Guid page, string title)
    {
        var current = await PagesApi.OkAsync(await PagesApi.GetAsync(_world.Admin, _world.Site, page));
        await PagesApi.OkAsync(await PagesApi.UpdateAsync(_world.Admin, _world.Site, page, title, current.GetProperty("slug").GetString()!, current.GetProperty("version").GetInt32()));
        await _world.PublishDocumentAsync(page, PublishingApi.HeadingDocument(title));
    }

    private async Task<JsonElement> GraphAsync(string address)
    {
        var html = (await _world.VisitAsync(address)).Body;
        var json = JsonLd().Match(html);
        Assert.True(json.Success, $"{address} has no JSON-LD.");
        using var parsed = JsonDocument.Parse(json.Groups[1].Value);
        return parsed.RootElement.GetProperty("@graph").Clone();
    }

    private static JsonElement Node(JsonElement graph, string type) =>
        graph.EnumerateArray().Single(n => n.GetProperty("@type").GetString() == type);

    private static string[] Crumbs(JsonElement graph) =>
        [.. Node(graph, "BreadcrumbList").GetProperty("itemListElement").EnumerateArray().Select(i => i.GetProperty("name").GetString()!)];

    private long Renders => _world.App.Services.GetRequiredService<RenderMetrics>().Renders;

    [Fact]
    public async Task The_home_page_describes_the_website_and_other_pages_their_trail()
    {
        Assert.Equal("WebSite", (await GraphAsync("/")).EnumerateArray().First().GetProperty("@type").GetString());
        Assert.Equal(["Home", "About", "Team"], Crumbs(await GraphAsync("/about/team")));
    }

    [Fact]
    public async Task An_item_page_describes_its_item_and_its_trail_ends_with_the_item()
    {
        var graph = await GraphAsync("/blog/hello");

        Assert.Equal(["Home", "Blog", PagesApi.Title("hello")], Crumbs(graph));
        var article = Node(graph, "Article");
        Assert.Equal(PagesApi.Title("hello"), article.GetProperty("headline").GetString());
        Assert.Equal($"https://{CmsScenario.HostA}/blog/hello", article.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Renaming_and_publishing_a_parent_refreshes_its_descendants_and_leaves_others_cached()
    {
        await GraphAsync("/about/team");
        await GraphAsync("/blog");
        var before = Renders;

        await PublishTitledAsync(_about, "Company");
        var teamGraph = await GraphAsync("/about/team");
        await GraphAsync("/blog");

        Assert.Equal(["Home", "Company", "Team"], Crumbs(teamGraph));
        // The child renders again; the blog, elsewhere in the tree, stays cached.
        Assert.Equal(before + 1, Renders);
    }

    [Fact]
    public async Task Republishing_a_parent_without_a_title_change_refreshes_nothing_under_it()
    {
        await GraphAsync("/about/team");
        var before = Renders;

        await _world.PublishDocumentAsync(_about, PublishingApi.HeadingDocument("About"));
        await GraphAsync("/about/team");

        Assert.Equal(before, Renders);
    }

    [Fact]
    public async Task A_new_home_page_title_refreshes_every_live_page()
    {
        await GraphAsync("/blog");
        await PublishTitledAsync(_home, "Start");

        Assert.Equal(["Start", "Blog"], Crumbs(await GraphAsync("/blog")));
    }

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLd();
}
