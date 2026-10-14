using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 3: robots.txt, and the site's search visibility (017 FR-025, FR-027, FR-006;
/// research SE-16, SE-17; public-seo.md §6).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RobotsTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "seo-robots").ConfigureAwait(false);
        var pages = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nabout").ConfigureAwait(false);
        var about = PagesApi.PagePath(_scenario.SiteA, pages["/about"]);
        await PublishingApi.SaveAsync(_scenario.Admin, about, PublishingApi.HeadingDocument("About")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, about).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Visitor => _scenario.App.CreateClientForHost(PagesScenario.HostA);

    private Task<string> RobotsAsync() => Visitor.GetStringAsync(new Uri("/robots.txt", UriKind.Relative));

    [Fact]
    public async Task The_default_allows_the_site_keeps_crawlers_out_of_the_admin_and_names_the_sitemap()
    {
        var response = await Visitor.GetAsync(new Uri("/robots.txt", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $"User-agent: *\nDisallow: /api/\nDisallow: /admin/\nAllow: /\n\nSitemap: https://{PagesScenario.HostA}/sitemap.xml\n",
            await response.Content.ReadAsStringAsync().ConfigureAwait(true));
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task Custom_rules_follow_the_generated_ones()
    {
        await SeoApi.SetSearchVisibilityAsync(_scenario.Admin, _scenario.SiteA, robotsRules: "User-agent: BadBot\r\nDisallow: /\r\n").ConfigureAwait(true);

        var text = await RobotsAsync().ConfigureAwait(true);

        Assert.EndsWith("\n# Custom rules\nUser-agent: BadBot\nDisallow: /\n", text, StringComparison.Ordinal);
        Assert.Contains("Sitemap: ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discouraging_search_engines_disallows_everything_and_marks_every_page()
    {
        await SeoApi.SetSearchVisibilityAsync(_scenario.Admin, _scenario.SiteA, discourage: true).ConfigureAwait(true);

        Assert.StartsWith("User-agent: *\nDisallow: /\n\nSitemap: ", await RobotsAsync().ConfigureAwait(true), StringComparison.Ordinal);

        var page = await Visitor.GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal("noindex, nofollow", page.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("noindex, nofollow", SeoApi.Head(await page.Content.ReadAsStringAsync().ConfigureAwait(true), "name", "robots"));
    }

    [Theory]
    [InlineData(10 * 1024 + 1, 1)]
    [InlineData(501, 1)]
    public async Task Robots_rules_beyond_the_limits_are_refused(int length, int lines)
    {
        var rules = string.Join('\n', Enumerable.Repeat(new string('x', length), lines));
        var version = await SeoApi.SiteVersionAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var response = await SeoApi.PutSearchVisibilityAsync(_scenario.Admin, _scenario.SiteA, version, robotsRules: rules).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "sites.search_invalid").ConfigureAwait(true);
    }

    [Fact]
    public async Task Only_administrators_change_search_visibility()
    {
        var designer = await _scenario.UserAsync((_scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);
        var version = await SeoApi.SiteVersionAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);

        var response = await SeoApi.PutSearchVisibilityAsync(designer, _scenario.SiteA, version, discourage: true).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.Forbidden, "identity.forbidden").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        var version = await SeoApi.SiteVersionAsync(_scenario.Admin, _scenario.SiteA).ConfigureAwait(true);
        await SeoApi.SetSearchVisibilityAsync(_scenario.Admin, _scenario.SiteA, discourage: true).ConfigureAwait(true);

        var response = await SeoApi.PutSearchVisibilityAsync(_scenario.Admin, _scenario.SiteA, version).ConfigureAwait(true);

        await PagesApi.RefusedAsync(response, HttpStatusCode.Conflict, "sites.out_of_date").ConfigureAwait(true);
    }
}
