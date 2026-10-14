using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Shared.Infrastructure.Outbox;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 1: a published page carries its search and sharing metadata, with fallbacks, and
/// only after publish (017 FR-001 to FR-005, SC-007; research SE-05, SE-08).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PageMetadataRenderTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private string _servicesPath = string.Empty;
    private Guid _imageId;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "seo-metadata").ConfigureAwait(false);
        var ids = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nservices\nother").ConfigureAwait(false);
        _servicesPath = PagesApi.PagePath(_scenario.SiteA, ids["/services"]);

        var image = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "team.png").ConfigureAwait(false);
        await LibraryApi.SetAltTextAsync(_scenario.Admin, _scenario.SiteA, image.Id, "A team at work").ConfigureAwait(false);
        _imageId = image.Id;

        await PublishingApi.SaveAsync(_scenario.Admin, _servicesPath, PublishingApi.HeadingDocument("Services")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _servicesPath).ConfigureAwait(false);

        var otherPath = PagesApi.PagePath(_scenario.SiteA, ids["/other"]);
        await PublishingApi.SaveAsync(_scenario.Admin, otherPath, PublishingApi.HeadingDocument("Other")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, otherPath).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private Task<string> PageAsync(string path) =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetStringAsync(new Uri(path, UriKind.Relative));

    [Fact]
    public async Task A_published_page_carries_every_field()
    {
        await PublishSeoAsync($$"""
            { "title": "Web design in Hanoi", "description": "Fast sites.", "ogTitle": "Design",
              "ogDescription": "Fast.", "ogImage": { "mediaId": "{{_imageId}}" } }
            """).ConfigureAwait(true);

        var html = await PageAsync("/services").ConfigureAwait(true);

        Assert.Equal("Web design in Hanoi", SeoApi.Title(html));
        Assert.Equal("Fast sites.", SeoApi.Head(html, "name", "description"));
        Assert.Equal("https://pages-a.test/services", SeoApi.Head(html, "rel", "canonical", "href"));
        Assert.Equal("website", SeoApi.Head(html, "property", "og:type"));
        Assert.Equal("Design", SeoApi.Head(html, "property", "og:title"));
        Assert.Equal("Fast.", SeoApi.Head(html, "property", "og:description"));
        Assert.Equal("https://pages-a.test/services", SeoApi.Head(html, "property", "og:url"));
        Assert.Equal("Site A", SeoApi.Head(html, "property", "og:site_name"));
        Assert.Equal("en", SeoApi.Head(html, "property", "og:locale"));
        Assert.StartsWith("https://pages-a.test/_media/", SeoApi.Head(html, "property", "og:image"), StringComparison.Ordinal);
        Assert.Equal("A team at work", SeoApi.Head(html, "property", "og:image:alt"));
        Assert.Null(SeoApi.Head(html, "name", "robots"));
    }

    [Fact]
    public async Task Empty_fields_fall_back_and_write_no_empty_tags()
    {
        await PublishSeoAsync("""{ "title": "Only a title" }""").ConfigureAwait(true);

        var html = await PageAsync("/services").ConfigureAwait(true);

        Assert.Equal("Only a title", SeoApi.Head(html, "property", "og:title"));
        Assert.Null(SeoApi.Head(html, "name", "description"));
        Assert.Null(SeoApi.Head(html, "property", "og:description"));
        Assert.Null(SeoApi.Head(html, "property", "og:image"));
    }

    [Fact]
    public async Task A_draft_change_shows_only_after_publish_and_rollback_restores_it()
    {
        await PublishSeoAsync("""{ "title": "First" }""").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_scenario.Admin, _servicesPath, SeoApi.DocumentWithSeo("Services", """{ "title": "Second" }""")).ConfigureAwait(true);

        Assert.Equal("First", SeoApi.Title(await PageAsync("/services").ConfigureAwait(true)));

        var published = await PublishingApi.PublishCurrentAsync(_scenario.Admin, _servicesPath).ConfigureAwait(true);
        Assert.Equal("Second", SeoApi.Title(await PageAsync("/services").ConfigureAwait(true)));

        var previous = published.GetProperty("liveNumber").GetInt32() - 1;
        var rollback = await PublishingApi.RollbackAsync(_scenario.Admin, _servicesPath, previous).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("First", SeoApi.Title(await PageAsync("/services").ConfigureAwait(true)));
    }

    [Fact]
    public async Task A_site_title_suffix_shows_on_every_page_without_its_own_title_at_the_next_request()
    {
        await PublishSeoAsync("""{ "title": "Web design in Hanoi" }""").ConfigureAwait(true);
        Assert.Equal(PagesApi.Title("other"), SeoApi.Title(await PageAsync("/other").ConfigureAwait(true)));

        await SeoApi.SetSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, titleSuffix: " | Acme").ConfigureAwait(true);

        Assert.Equal(PagesApi.Title("other") + " | Acme", SeoApi.Title(await PageAsync("/other").ConfigureAwait(true)));
        Assert.Equal("Web design in Hanoi", SeoApi.Title(await PageAsync("/services").ConfigureAwait(true)));
    }

    [Fact]
    public async Task The_site_default_image_is_used_when_a_page_sets_none()
    {
        await SeoApi.SetSearchSettingsAsync(_scenario.Admin, _scenario.SiteA, defaultImageId: _imageId).ConfigureAwait(true);

        var html = await PageAsync("/other").ConfigureAwait(true);

        Assert.StartsWith("https://pages-a.test/_media/", SeoApi.Head(html, "property", "og:image"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deleted_sharing_image_leaves_the_page_working_without_one()
    {
        var spare = await LibraryApi.UploadNamedAsync(_scenario.Admin, _scenario.SiteA, "spare.png").ConfigureAwait(true);
        await PublishSeoAsync($$"""{ "ogImage": { "mediaId": "{{spare.Id}}" } }""").ConfigureAwait(true);
        Assert.NotNull(SeoApi.Head(await PageAsync("/services").ConfigureAwait(true), "property", "og:image"));

        var deleted = await UsageScenario.DeleteAsync(_scenario.Admin, _scenario.SiteA, spare.Id, confirmInUse: true).ConfigureAwait(true);
        Assert.True(deleted.IsSuccessStatusCode, $"delete answered {(int)deleted.StatusCode}");

        // The delete reaches Pages through the outbox (012 data-model §3.2).
        await DispatchAsync().ConfigureAwait(true);

        var response = await _scenario.App.CreateClientForHost(PagesScenario.HostA).GetAsync(new Uri("/services", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(SeoApi.Head(await response.Content.ReadAsStringAsync().ConfigureAwait(true), "property", "og:image"));
    }

    [Fact]
    public async Task Markup_in_metadata_is_text_everywhere()
    {
        await PublishSeoAsync("""{ "title": "\"><script>x</script>", "description": "a & b < c" }""").ConfigureAwait(true);

        var html = await PageAsync("/services").ConfigureAwait(true);

        Assert.DoesNotContain("<script>x", html, StringComparison.Ordinal);
        Assert.Equal("\"><script>x</script>", SeoApi.Title(html));
        Assert.Equal("a & b < c", SeoApi.Head(html, "name", "description"));
    }

    [Fact]
    public async Task A_hidden_page_asks_not_to_be_indexed()
    {
        await PublishSeoAsync("""{ "noindex": true }""").ConfigureAwait(true);

        Assert.Equal("noindex", SeoApi.Head(await PageAsync("/services").ConfigureAwait(true), "name", "robots"));
    }

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_scenario.App.Services);
        var settings = _scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    private async Task PublishSeoAsync(string seo)
    {
        await PublishingApi.SaveAsync(_scenario.Admin, _servicesPath, SeoApi.DocumentWithSeo("Services", seo)).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _servicesPath).ConfigureAwait(false);
    }
}
