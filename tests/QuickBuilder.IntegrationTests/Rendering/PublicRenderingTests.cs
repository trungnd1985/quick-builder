using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using Xunit;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// User Story 1: visitors get the published page as complete HTML at its address (012 FR-022,
/// FR-024, FR-025; public-rendering.md §1–§3).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublicRenderingTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private string _aboutPath = string.Empty;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "public").ConfigureAwait(false);
        var ids = await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "home*\nabout\ndraft-only").ConfigureAwait(false);
        _aboutPath = PagesApi.PagePath(_scenario.SiteA, ids["/about"]);

        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("About <us> & friends")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Visitor(string host = PagesScenario.HostA) => _scenario.App.CreateClientForHost(host);

    [Fact]
    public async Task A_published_page_is_complete_html_with_the_security_headers()
    {
        var response = await Visitor().GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.Contains("About &lt;us&gt; &amp; friends</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"qb-canvas qb-page\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.Ordinal);

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.StartsWith("default-src 'none'; style-src 'self' 'sha256-", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Theory]
    [InlineData("/About")]
    [InlineData("/about/")]
    [InlineData("/about?x=1")]
    public async Task Case_a_trailing_slash_and_a_query_serve_the_same_page(string address)
    {
        var response = await Visitor().GetAsync(new Uri(address, UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("About &lt;us&gt;", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Head_gets_the_headers_without_the_body()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri("/about", UriKind.Relative));
        var response = await Visitor().SendAsync(request).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Other_methods_are_not_allowed()
    {
        var response = await Visitor().PostAsync(new Uri("/about", UriKind.Relative), content: null).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("GET, HEAD", string.Join(", ", response.Content.Headers.Allow));
    }

    [Theory]
    [InlineData("/draft-only")]
    [InlineData("/no-such-page")]
    [InlineData("/")]
    public async Task Anything_not_published_is_not_found(string address)
    {
        var response = await Visitor().GetAsync(new Uri(address, UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_page_of_one_site_is_never_served_on_another()
    {
        var response = await Visitor(PagesScenario.HostB).GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("About", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_draft_change_is_not_visible_until_it_is_published()
    {
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("Changed")).ConfigureAwait(true);

        Assert.DoesNotContain(">Changed<", await Visitor().GetStringAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true), StringComparison.Ordinal);

        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        Assert.Contains(">Changed<", await Visitor().GetStringAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_stylesheet_the_page_links_to_is_served_and_cached_for_a_year()
    {
        var html = await Visitor().GetStringAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);
        var href = System.Text.RegularExpressions.Regex.Match(html, "href=\"(/_assets/blocks\\.[A-Za-z0-9_-]{12}\\.css)\"").Groups[1].Value;

        var css = await Visitor().GetAsync(new Uri(href, UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", css.Headers.CacheControl?.ToString());
        Assert.Contains(".qb-page", await css.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await Visitor().GetAsync(new Uri("/_assets/blocks.wrong.css", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_disabled_site_answers_as_an_unknown_host()
    {
        await _scenario.Admin.PostAsync(new Uri($"/api/v1/sites/{_scenario.SiteA}/disable", UriKind.Relative), content: null).ConfigureAwait(true);

        var response = await Visitor().GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("sites.not_resolved", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }
    [Fact]
    public async Task A_current_copy_is_answered_with_304_until_the_page_changes()
    {
        var first = await Visitor().GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.False(etag.IsWeak);
        Assert.Equal("public, no-cache", first.Headers.CacheControl?.ToString());

        using var again = new HttpRequestMessage(HttpMethod.Get, new Uri("/about", UriKind.Relative));
        again.Headers.IfNoneMatch.Add(etag);
        var notModified = await Visitor().SendAsync(again).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
        Assert.Equal(etag, notModified.Headers.ETag);

        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("Changed")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        using var stale = new HttpRequestMessage(HttpMethod.Get, new Uri("/about", UriKind.Relative));
        stale.Headers.IfNoneMatch.Add(etag);
        var changed = await Visitor().SendAsync(stale).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(etag, changed.Headers.ETag);
        Assert.Contains(">Changed<", await changed.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_are_still_served_when_the_cache_fails()
    {
        using var app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("public-no-cache").ConfigureAwait(true),
            configureTestServices: services => services.AddSingleton<ICacheProvider, FailingCache>());
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
        var site = await MediaApi.CreateSiteAsync(admin, "No cache", "no-cache.test").ConfigureAwait(true);
        var page = (await PagesApi.CreateOkAsync(admin, site, "about").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var path = PagesApi.PagePath(site, page);
        await PublishingApi.SaveAsync(admin, path, PublishingApi.HeadingDocument("Still here")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(admin, path).ConfigureAwait(true);

        var response = await app.CreateClientForHost("no-cache.test").GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(">Still here<", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    /// <summary>A cache whose every call fails, as a broken shared cache would (FR-034).</summary>
    private sealed class FailingCache : ICacheProvider
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, Func<T, CacheEntrySettings> settings, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The cache is down.");

        public void Remove(string key) => throw new InvalidOperationException("The cache is down.");
    }

}

/// <summary>
/// The catch-all page route never takes over the administrative surface (012 research PR-08,
/// analysis U1).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublicRouteBoundaryTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;

    public async Task InitializeAsync() =>
        _scenario = await PagesScenario.CreateAsync(database, "boundary").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_admin_deep_link_still_returns_the_admin_shell()
    {
        var response = await _scenario.App.CreateAnonymousClient()
            .GetAsync(new Uri($"/admin/sites/{_scenario.SiteA}/pages", UriKind.Relative)).ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        // Never the page route's answer, whether or not the admin app was built.
        Assert.DoesNotContain("Page not found</h1>", body, StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.False(
            response.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.Single().StartsWith("default-src 'none'", StringComparison.Ordinal),
            "An admin deep link got a published page's policy.");

        // The shell itself is there only when the admin app has been built into wwwroot.
        var shell = Path.Combine(AppContext.BaseDirectory, "wwwroot", "admin", "index.html");
        if (File.Exists(shell))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<div id=\"app\"", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_unknown_api_path_is_not_a_page()
    {
        var response = await _scenario.Admin.GetAsync(new Uri("/api/v1/no-such-thing", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<!doctype", await response.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_still_answers()
    {
        var response = await _scenario.App.CreateAnonymousClient().GetAsync(new Uri("/health/live", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void The_page_route_leaves_exactly_the_hosts_administrative_prefixes_alone()
    {
        Assert.Equal(
            QuickBuilder.Host.Routing.RoutePrefixes.ReservedPrefixes.Select(p => p.TrimStart('/')).Order(StringComparer.Ordinal),
            QuickBuilder.Rendering.Features.RenderPage.PublicPathConstraint.AdministrativeSegments.Order(StringComparer.Ordinal));
    }
}
