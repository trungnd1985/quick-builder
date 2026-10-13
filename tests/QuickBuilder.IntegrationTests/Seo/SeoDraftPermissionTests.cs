using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// Editors write metadata; the canonical address and hiding are Designers' (017 FR-002,
/// research SE-03).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeoDraftPermissionTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private string _pagePath = string.Empty;
    private HttpClient _editor = null!;
    private HttpClient _designer = null!;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "seo-permissions").ConfigureAwait(false);
        var ids = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nservices").ConfigureAwait(false);
        _pagePath = PagesApi.PagePath(_scenario.SiteA, ids["/services"]);
        _editor = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Editor)).ConfigureAwait(false);
        _designer = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_editor_may_change_the_texts()
    {
        var response = await PagesApi.SaveCurrentDraftAsync(
            _editor, _pagePath, SeoApi.DocumentWithSeo("Services", """{ "title": "T", "description": "D", "noSitemap": true }""")).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("""{ "canonical": { "type": "url", "href": "https://elsewhere.test/" } }""", "canonical")]
    [InlineData("""{ "noindex": true }""", "noindex")]
    [InlineData("""{ "bind": { "noindex": { "value": { "field": "f1" } } } }""", "bind.noindex")]
    public async Task An_editor_may_not_change_the_canonical_address_or_hiding(string seo, string field)
    {
        var response = await PagesApi.SaveCurrentDraftAsync(_editor, _pagePath, SeoApi.DocumentWithSeo("Services", seo)).ConfigureAwait(true);

        var problem = await PagesApi.RefusedAsync(response, HttpStatusCode.Forbidden, "pages.seo_restricted").ConfigureAwait(true);
        Assert.Equal(field, problem.Text("field"));
    }

    [Fact]
    public async Task An_editor_may_save_a_page_whose_restricted_fields_a_designer_set()
    {
        const string seo = """{ "noindex": true, "canonical": { "type": "url", "href": "https://elsewhere.test/" } }""";
        await PublishingApi.SaveAsync(_designer, _pagePath, SeoApi.DocumentWithSeo("Services", seo)).ConfigureAwait(true);

        const string edited = """{ "title": "New", "noindex": true, "canonical": { "type": "url", "href": "https://elsewhere.test/" } }""";
        var response = await PagesApi.SaveCurrentDraftAsync(_editor, _pagePath, SeoApi.DocumentWithSeo("Services, edited", edited)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_editor_may_not_remove_hiding_either()
    {
        await PublishingApi.SaveAsync(_designer, _pagePath, SeoApi.DocumentWithSeo("Services", """{ "noindex": true }""")).ConfigureAwait(true);

        var response = await PagesApi.SaveCurrentDraftAsync(_editor, _pagePath, SeoApi.DocumentWithSeo("Services", """{ "title": "T" }""")).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.Forbidden, "pages.seo_restricted").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_designer_may_change_them()
    {
        var response = await PagesApi.SaveCurrentDraftAsync(
            _designer, _pagePath, SeoApi.DocumentWithSeo("Services", """{ "noindex": true, "canonical": { "type": "url", "href": "https://elsewhere.test/" } }""")).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
