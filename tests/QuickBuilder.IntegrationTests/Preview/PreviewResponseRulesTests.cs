using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Rendering.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// User Story 3: previews stay out of search engines, caches and the public site (022 FR-022–
/// FR-025, SC-004; public-preview.md §5, §6).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewResponseRulesTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _pricing;
    private Guid _post;
    private CreatedLink _pageLink = null!;
    private MediaFileDtoRef _image = null!;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "preview-rules").ConfigureAwait(false);
        var image = await MediaApi.UploadOkAsync(_world.Admin, _world.Site).ConfigureAwait(false);
        _image = new MediaFileDtoRef(image.Id, image.PublicPath["/_media/".Length..]);

        _pricing = await _world.PageAsync("pricing").ConfigureAwait(false);
        await _world.PublishDocumentAsync(_pricing, PublishingApi.HeadingDocument("Old prices")).ConfigureAwait(false);
        var draft = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":["""
            + """{"id":"h1","type":"heading","props":{"text":"New prices","level":1},"children":[]},"""
            + $$$"""{"id":"i1","type":"image","props":{"media":{"mediaId":"{{{image.Id}}}"}},"children":[]}"""
            + "]}]}}";
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, _pricing), draft).ConfigureAwait(false);

        var blog = await _world.PageAsync("blog").ConfigureAwait(false);
        await _world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(false);
        var template = await _world.PageAsync("post", blog).ConfigureAwait(false);
        await _world.TemplateOkAsync(template, _world.Posts).ConfigureAwait(false);
        await _world.PublishDocumentAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("h", """{"field":"f1"}"""))).ConfigureAwait(false);
        _post = ItemsApi.Id(await _world.Items.PostAsync("hello", new JsonObject { ["f1"] = "Draft post" }).ConfigureAwait(false));

        _pageLink = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", _pricing, DateTimeOffset.UtcNow.AddDays(1)).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private async Task<HttpResponseMessage> AdminGetAsync(string path) =>
        await _world.Admin.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);

    private async Task<HttpResponseMessage> VisitorGetAsync(string path) =>
        (await PreviewLinksApi.OpenAsync(_world.App, path, CmsScenario.HostA).ConfigureAwait(false)).Response;

    public static TheoryData<string> Documents => new() { "member-page", "member-item", "link-page", "link-gone", "link-later" };

    [Fact]
    public async Task Every_preview_response_is_never_stored_never_indexed_and_passes_no_address_on()
    {
        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["member page"] = await AdminGetAsync($"/_preview/sites/{_world.Site}/pages/{_pricing}").ConfigureAwait(true),
            ["member item"] = await AdminGetAsync($"/_preview/sites/{_world.Site}/items/{_post}").ConfigureAwait(true),
            ["member media"] = await AdminGetAsync($"/_preview/sites/{_world.Site}/media/{_image.File}").ConfigureAwait(true),
            ["link page"] = await VisitorGetAsync(_pageLink.Path).ConfigureAwait(true),
            ["link media"] = await VisitorGetAsync($"{_pageLink.Path}/media/{_image.File}").ConfigureAwait(true),
            ["unknown link"] = await VisitorGetAsync("/_preview/l/" + new string('a', 43)).ConfigureAwait(true),
            ["other path"] = await VisitorGetAsync("/_preview/whatever").ConfigureAwait(true),
            ["stylesheet"] = await VisitorGetAsync($"/_preview/assets/{QuickBuilder.Rendering.Rendering.BlockStylesheet.Instance.FileName}").ConfigureAwait(true),
            ["member refused"] = await _world.App.CreateAnonymousClient().GetAsync(new Uri($"/_preview/sites/{_world.Site}/pages/{_pricing}", UriKind.Relative)).ConfigureAwait(true),
        };
        using (var form = _world.App.CreateClientForHost(CmsScenario.HostA))
        {
            responses["form"] = await form.PostAsync(new Uri("/_preview/form", UriKind.Relative), new StringContent("a=b")).ConfigureAwait(true);
        }

        foreach (var (name, response) in responses)
        {
            Assert.True(response.Headers.CacheControl?.ToString() == "no-store, private", $"{name}: Cache-Control {response.Headers.CacheControl}");
            Assert.True(response.Headers.TryGetValues("X-Robots-Tag", out var robots) && robots.Single() == "noindex, nofollow", $"{name}: X-Robots-Tag");
            Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var referrer) && referrer.Single() == "no-referrer", $"{name}: Referrer-Policy");
            if (!name.Contains("media", StringComparison.Ordinal))
            {
                Assert.True(response.Headers.ETag is null, $"{name}: ETag");
            }
        }

        Assert.Equal(HttpStatusCode.OK, responses["member page"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses["link media"].StatusCode);
    }

    [Fact]
    public async Task A_preview_document_says_noindex_and_no_referrer_even_when_the_page_allows_indexing()
    {
        foreach (var html in new[]
                 {
                     await (await AdminGetAsync($"/_preview/sites/{_world.Site}/pages/{_pricing}").ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true),
                     await (await VisitorGetAsync(_pageLink.Path).ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true),
                 })
        {
            Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\">", html, StringComparison.Ordinal);
            Assert.Contains("<meta name=\"referrer\" content=\"no-referrer\">", html, StringComparison.Ordinal);
        }

        var (_, published) = await _world.VisitAsync("/pricing").ConfigureAwait(true);
        Assert.DoesNotContain("noindex", published, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Previews_never_touch_the_public_cache_and_the_public_page_stays_published()
    {
        var metrics = _world.App.Services.GetRequiredService<RenderMetrics>();
        await _world.VisitAsync("/pricing").ConfigureAwait(true);
        await _world.VisitAsync("/pricing").ConfigureAwait(true);
        var warm = metrics.Renders;

        for (var i = 0; i < 10; i++)
        {
            await AdminGetAsync($"/_preview/sites/{_world.Site}/pages/{_pricing}").ConfigureAwait(true);
            await VisitorGetAsync(_pageLink.Path).ConfigureAwait(true);
        }

        var (status, published) = await _world.VisitAsync("/pricing").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Old prices</h1>", published, StringComparison.Ordinal);
        Assert.DoesNotContain("New prices", published, StringComparison.Ordinal);
        Assert.Equal(warm, metrics.Renders);
    }

    [Fact]
    public async Task Robots_txt_disallows_previews_and_the_sitemap_never_lists_them()
    {
        var (_, robots) = await _world.VisitAsync("/robots.txt").ConfigureAwait(true);
        var (_, sitemap) = await _world.VisitAsync("/sitemap.xml").ConfigureAwait(true);

        Assert.Contains("Disallow: /_preview/\n", robots, StringComparison.Ordinal);
        Assert.DoesNotContain("_preview", sitemap, StringComparison.Ordinal);
    }

    private sealed record MediaFileDtoRef(Guid Id, string File);
}
