using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 6: one address per page. Other letter cases and a trailing slash redirect to it
/// when content is there, and every page names it (017 FR-011 to FR-013; research SE-06).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AddressNormalisationTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _blog;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "seo-normalise");
        var home = await _world.PageAsync("home");
        _blog = await _world.PageAsync("blog");
        await _world.PublishDocumentAsync(_blog, PublishingApi.HeadingDocument("Blog"));
        var template = await _world.PageAsync("post", _blog);
        await _world.TemplateOkAsync(template, _world.Posts);
        await _world.PublishDocumentAsync(template, PublishingApi.HeadingDocument("Post"));
        await _world.PublishedPostAsync("hello", new JsonObject { ["f1"] = PagesApi.Title("hello") });
        _ = home;
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> VisitAsync(string address) => RedirectsApi.VisitAsync(_world.App, CmsScenario.HostA, address);

    [Theory]
    [InlineData("/Blog/", "/blog")]
    [InlineData("/blog/", "/blog")]
    [InlineData("/BLOG?utm=x&page=2", "/blog?utm=x&page=2")]
    [InlineData("/Blog/Hello", "/blog/hello")]
    [InlineData("/blog/hello/?a=1", "/blog/hello?a=1")]
    public async Task Another_case_or_a_trailing_slash_redirects_to_the_address_keeping_the_query(string address, string location)
    {
        var response = await VisitAsync(address);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task The_root_is_never_redirected()
    {
        Assert.NotEqual(HttpStatusCode.MovedPermanently, (await VisitAsync("/")).StatusCode);
    }

    [Fact]
    public async Task A_miss_in_another_case_goes_on_to_redirects_then_the_same_not_found_page()
    {
        await RedirectsApi.CreateOkAsync(_world.Admin, _world.Site, "/old-blog", RedirectsApi.ToPage(_blog));

        var redirected = await VisitAsync("/Old-Blog/");
        var lower = await VisitAsync("/nothing-here");
        var upper = await VisitAsync("/Nothing-Here/");

        Assert.Equal("/blog", redirected.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, upper.StatusCode);
        Assert.Equal(await lower.Content.ReadAsByteArrayAsync(), await upper.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Every_page_names_its_one_address_with_the_page_of_its_list()
    {
        var html = await (await VisitAsync("/blog?utm=campaign")).Content.ReadAsStringAsync();

        Assert.Equal($"https://{CmsScenario.HostA}/blog", SeoApi.Head(html, "rel", "canonical", "href"));
        Assert.Equal($"https://{CmsScenario.HostA}/blog", SeoApi.Head(html, "property", "og:url"));
    }

    [Fact]
    public async Task A_canonical_override_names_the_target_page()
    {
        var copy = await _world.PageAsync("copy");
        await _world.PublishDocumentAsync(copy, SeoApi.DocumentWithSeo("Copy", $$"""{ "canonical": { "type": "page", "pageId": "{{_blog}}" } }"""));

        var html = await (await VisitAsync("/copy")).Content.ReadAsStringAsync();

        Assert.Equal($"https://{CmsScenario.HostA}/blog", SeoApi.Head(html, "rel", "canonical", "href"));
    }
}
