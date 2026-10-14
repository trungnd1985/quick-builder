using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Infrastructure.Outbox;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 3: the sitemap follows every change to what it lists, at the next request, on
/// every instance (017 FR-026, SC-005; research SE-15).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SitemapFreshnessTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly XNamespace Sm = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private TemplatesWorld _world = null!;
    private Guid _blog;
    private Guid _about;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "seo-sitemap-fresh");
        _blog = await _world.PageAsync("blog");
        await _world.PublishDocumentAsync(_blog, PublishingApi.HeadingDocument("Blog"));
        var template = await _world.PageAsync("post", _blog);
        await _world.TemplateOkAsync(template, _world.Posts);
        await _world.PublishDocumentAsync(template, PublishingApi.HeadingDocument("Post"));
        _about = await _world.PageAsync("about");
        await _world.PublishDocumentAsync(_about, PublishingApi.HeadingDocument("About"));
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<string[]> PathsAsync(HttpClient visitor) =>
        [.. XDocument.Parse(await visitor.GetStringAsync(new Uri("/sitemap.xml", UriKind.Relative)).ConfigureAwait(false))
            .Descendants(Sm + "loc")
            .Select(l => new Uri(l.Value).AbsolutePath)
            .Order(StringComparer.Ordinal)];

    private Task<string[]> PathsAsync() => PathsAsync(_world.Visitor());

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_world.App.Services);
        var settings = _world.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    [Fact]
    public async Task Publishing_and_unpublishing_a_page_shows_at_the_next_request()
    {
        Assert.Equal(["/about", "/blog"], await PathsAsync().ConfigureAwait(true));

        await PublishingApi.UnpublishAsync(_world.Admin, PagesApi.PagePath(_world.Site, _about)).ConfigureAwait(true);
        Assert.Equal(["/blog"], await PathsAsync().ConfigureAwait(true));

        var fresh = await _world.PageAsync("fresh");
        await _world.PublishDocumentAsync(fresh, PublishingApi.HeadingDocument("Fresh"));
        Assert.Equal(["/blog", "/fresh"], await PathsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task A_slug_change_and_a_move_show_at_the_next_request()
    {
        await PathsAsync().ConfigureAwait(true);
        var page = await PagesApi.OkAsync(await PagesApi.GetAsync(_world.Admin, _world.Site, _about).ConfigureAwait(true)).ConfigureAwait(true);

        await PagesApi.UpdateAsync(_world.Admin, _world.Site, _about, "About", "about-us", page.GetProperty("version").GetInt32()).ConfigureAwait(true);

        Assert.Contains("/about-us", await PathsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Publishing_and_unpublishing_an_item_shows_once_its_event_is_handled()
    {
        await PathsAsync().ConfigureAwait(true);

        var post = await _world.PublishedPostAsync("new-post", new JsonObject { ["f1"] = "New" });
        await DispatchAsync().ConfigureAwait(true);
        Assert.Contains("/blog/new-post", await PathsAsync().ConfigureAwait(true));

        var unpublished = await ItemsApi.UnpublishAsync(_world.Admin, _world.Site, _world.Posts, post.GetProperty("id").GetGuid(), post.GetProperty("revision").GetInt32()).ConfigureAwait(true);
        Assert.True(unpublished.IsSuccessStatusCode, $"unpublish answered {(int)unpublished.StatusCode}");
        await DispatchAsync().ConfigureAwait(true);
        Assert.DoesNotContain("/blog/new-post", await PathsAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task A_second_instance_sees_the_change_at_its_next_request()
    {
        using var second = new QuickBuilderApp(_world.App.ConnectionString);
        var secondVisitor = second.CreateClientForHost(QuickBuilder.IntegrationTests.Cms.CmsScenario.HostA);
        Assert.Contains("/about", await PathsAsync(secondVisitor).ConfigureAwait(true));

        await PublishingApi.UnpublishAsync(_world.Admin, PagesApi.PagePath(_world.Site, _about)).ConfigureAwait(true);

        Assert.DoesNotContain("/about", await PathsAsync(secondVisitor).ConfigureAwait(true));
    }

    [Fact]
    public async Task Hiding_a_page_takes_it_out_once_published()
    {
        await PathsAsync().ConfigureAwait(true);

        await _world.PublishDocumentAsync(_about, SeoApi.DocumentWithSeo("About", """{ "noindex": true }"""));

        Assert.DoesNotContain("/about", await PathsAsync().ConfigureAwait(true));
    }
}
