using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 4: metadata templates. A template's metadata binds to item fields, so each item
/// page has its own, and a boolean field hides items one by one (017 FR-007, FR-007a, FR-008;
/// research SE-04; seo-metadata.md §3).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeoTemplateRenderTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Prefix = PagesApi.CanaryMarker;

    private const string TemplateSeo =
        """
        {
          "bind": {
            "title": { "value": { "parts": [ { "field": "f1" }, " | Blog" ] } },
            "description": { "value": { "field": "f2" } },
            "ogImage": { "value": { "field": "f6" } },
            "noindex": { "value": { "field": "f4" } }
          },
          "fallback": { "description": "A post on our blog." }
        }
        """;

    private TemplatesWorld _world = null!;
    private Guid _template;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "seo-template");
        var blog = await _world.PageAsync("blog");
        await _world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog"));
        _template = await _world.PageAsync("post", blog);
        await _world.TemplateOkAsync(_template, _world.Posts);
        await _world.PublishDocumentAsync(_template, SeoApi.DocumentWithSeo("Post", TemplateSeo));
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private static JsonNode Body(string text) => JsonNode.Parse(
        $$"""{ "type": "doc", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "{{text}}" } ] }, { "type": "paragraph", "content": [ { "type": "text", "text": "More   words" } ] } ] }""")!;

    [Fact]
    public async Task Each_item_page_shows_its_own_bound_metadata_from_published_data()
    {
        await _world.PublishedPostAsync("hello", new JsonObject
        {
            ["f1"] = $"{Prefix}Hello",
            ["f2"] = Body($"{Prefix}Intro"),
            ["f6"] = new JsonObject { ["mediaId"] = _world.Items.Image.ToString() },
        });

        var (status, html) = await _world.VisitAsync("/blog/hello");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal($"{Prefix}Hello | Blog", SeoApi.Title(html));
        Assert.Equal($"{Prefix}Intro More words", SeoApi.Head(html, "name", "description"));
        Assert.Equal($"{Prefix}Hello | Blog", SeoApi.Head(html, "property", "og:title"));
        Assert.Contains(_world.Items.Image.ToString("N"), SeoApi.Head(html, "property", "og:image"), StringComparison.Ordinal);
        Assert.Null(SeoApi.Head(html, "name", "robots"));
    }

    [Fact]
    public async Task An_empty_value_uses_the_fallback_text()
    {
        await _world.PublishedPostAsync("bare", new JsonObject { ["f1"] = $"{Prefix}Bare" });

        var (_, html) = await _world.VisitAsync("/blog/bare");

        Assert.Equal("A post on our blog.", SeoApi.Head(html, "name", "description"));
    }

    [Fact]
    public async Task The_hide_switch_hides_that_item_only_from_the_head_and_the_sitemap()
    {
        await _world.PublishedPostAsync("secret", new JsonObject { ["f1"] = $"{Prefix}Secret", ["f4"] = true });
        await _world.PublishedPostAsync("shown", new JsonObject { ["f1"] = $"{Prefix}Shown", ["f4"] = false });

        Assert.Equal("noindex", SeoApi.Head((await _world.VisitAsync("/blog/secret")).Body, "name", "robots"));
        Assert.Null(SeoApi.Head((await _world.VisitAsync("/blog/shown")).Body, "name", "robots"));

        XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var sitemap = XDocument.Parse(await _world.Visitor().GetStringAsync(new Uri("/sitemap.xml", UriKind.Relative)));
        var paths = sitemap.Descendants(sm + "loc").Select(l => new Uri(l.Value).AbsolutePath).ToList();
        Assert.Contains("/blog/shown", paths);
        Assert.DoesNotContain("/blog/secret", paths);
    }

    [Fact]
    public async Task Republishing_an_item_changes_its_metadata_and_leaves_other_items_pages_cached()
    {
        var first = await _world.PublishedPostAsync("first", new JsonObject { ["f1"] = $"{Prefix}Before" });
        await _world.PublishedPostAsync("second", new JsonObject { ["f1"] = $"{Prefix}Second" });
        using var visitor = _world.Visitor();
        using var firstBefore = await visitor.GetAsync(new Uri("/blog/first", UriKind.Relative));
        using var secondBefore = await visitor.GetAsync(new Uri("/blog/second", UriKind.Relative));

        var saved = await ItemsApi.SaveOkAsync(_world.Admin, _world.Site, _world.Posts, ItemsApi.Id(first),
            ItemsApi.Draft("first", new JsonObject { ["f1"] = $"{Prefix}After" }, _world.Items.PostsSchemaRevision, ItemsApi.Revision(first)));
        await ItemsApi.PublishOkAsync(_world.Admin, _world.Site, _world.Posts, ItemsApi.Id(saved), ItemsApi.Revision(saved));

        using var firstAfter = await visitor.GetAsync(new Uri("/blog/first", UriKind.Relative));
        using var secondAfter = await visitor.GetAsync(new Uri("/blog/second", UriKind.Relative));

        Assert.Equal($"{Prefix}After | Blog", SeoApi.Title(await firstAfter.Content.ReadAsStringAsync()));
        Assert.NotEqual(firstBefore.Headers.ETag, firstAfter.Headers.ETag);
        Assert.Equal(secondBefore.Headers.ETag, secondAfter.Headers.ETag);
    }

    [Fact]
    public async Task Metadata_bindings_count_in_the_impact_of_turning_the_template_back_into_a_page()
    {
        var impact = await PagesApi.OkAsync(await _world.Admin.GetAsync(new Uri(_world.TemplatePath(_template), UriKind.Relative)));

        // title (f1), description (f2), ogImage (f6) and noindex (f4) all read the page's item.
        Assert.Equal(4, impact.GetProperty("brokenBindings").GetInt32());
    }

    [Fact]
    public async Task Metadata_bindings_on_an_ordinary_page_are_not_read_and_its_own_metadata_shows()
    {
        var about = await _world.PageAsync("about");
        await _world.PublishDocumentAsync(about, SeoApi.DocumentWithSeo(
            "About",
            """{ "title": "About us", "bind": { "title": { "value": { "field": "f1" } }, "noindex": { "value": { "field": "f4" } } } }"""));

        var (status, html) = await _world.VisitAsync("/about");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("About us", SeoApi.Title(html));
        Assert.Null(SeoApi.Head(html, "name", "robots"));
    }
}
