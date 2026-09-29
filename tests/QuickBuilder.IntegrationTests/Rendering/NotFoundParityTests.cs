using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Sites;
using Xunit;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// User Story 7: one not-found page for every missing address on a site, in its design and
/// language, and the same bytes for the administrative prefixes on a client domain (012
/// FR-030, research PR-08, PR-13, 009 S-13; public-rendering.md §4).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class NotFoundParityTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string AdminHost = "admin.test";
    private const string ClientHost = "client.test";

    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;
    private Guid _site;
    private Dictionary<string, Guid> _pages = [];

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("not-found").ConfigureAwait(false),
            settings: new Dictionary<string, string> { ["Hosting:AdminHostNames:0"] = AdminHost });
        _admin = await _app.CreateAdminClientAsync(AdminHost).ConfigureAwait(false);
        _site = (await SitesApi.CreateOkAsync(_admin, "Client", [ClientHost]).ConfigureAwait(false)).GetProperty("id").GetGuid();

        _pages = await PagesApi.SeedTreeAsync(_admin, _site, "home*\nunpublished\ndraft-only").ConfigureAwait(false);
        var unpublished = PagesApi.PagePath(_site, _pages["/unpublished"]);
        await PublishingApi.PublishCurrentAsync(_admin, unpublished).ConfigureAwait(false);
        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_admin, unpublished).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/sites")]
    [InlineData("/api/x")]
    [InlineData("/unpublished")]
    [InlineData("/draft-only")]
    [InlineData("/never/used?q=1")]
    [InlineData("/_assets/not-a-stylesheet.css")]
    public async Task Every_missing_address_gets_the_same_bytes(string path)
    {
        var reference = await VisitAsync("/no-such-page").ConfigureAwait(true);
        var response = await VisitAsync(path).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(await reference.Content.ReadAsByteArrayAsync().ConfigureAwait(true), await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
        Assert.Equal(reference.Content.Headers.ContentType?.ToString(), response.Content.Headers.ContentType?.ToString());
        Assert.Equal(reference.Headers.GetValues("Content-Security-Policy").Single(), response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task It_is_an_html_page_in_the_sites_design_that_is_never_stored()
    {
        var response = await VisitAsync("/no-such-page?secret=value").ConfigureAwait(true);
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Null(response.Headers.ETag);
        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<title>Page not found</title>", html, StringComparison.Ordinal);
        Assert.Contains(">Page not found</h1>", html, StringComparison.Ordinal);
        Assert.Contains("--qb-color-color-brand:", html, StringComparison.Ordinal);
        Assert.StartsWith("default-src 'none'; style-src 'self' 'sha256-", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());

        // Nothing of the request comes back.
        Assert.DoesNotContain("no-such-page", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.Ordinal);
        Assert.DoesNotContain(ClientHost, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_link_home_is_there_only_while_the_home_page_is_live()
    {
        Assert.DoesNotContain("Go to the home page", await BodyAsync().ConfigureAwait(true), StringComparison.Ordinal);

        var home = PagesApi.PagePath(_site, _pages["/"]);
        await PublishingApi.PublishCurrentAsync(_admin, home).ConfigureAwait(true);
        var withHome = await BodyAsync().ConfigureAwait(true);
        Assert.Contains("href=\"/\">Go to the home page</a>", withHome, StringComparison.Ordinal);

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_admin, home).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.DoesNotContain("Go to the home page", await BodyAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_speaks_the_sites_language()
    {
        var site = await SitesApi.GetOkAsync(_admin, _site).ConfigureAwait(true);
        await PagesApi.OkAsync(await SitesApi.PutSettingsAsync(
            _admin, _site, "Client", site.GetProperty("scheme").GetString()!, "de",
            site.GetProperty("primaryHostName").GetString()!, site.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        var html = await BodyAsync().ConfigureAwait(true);

        Assert.Contains("<html lang=\"de\">", html, StringComparison.Ordinal);
        Assert.Contains(">Seite nicht gefunden</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_host_and_a_disabled_site_keep_the_json_answer()
    {
        var unknown = await _app.CreateClientForHost("nobody.test").GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        var unknownBody = await unknown.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.DoesNotContain("<!doctype", unknownBody, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await SitesApi.DisableAsync(_admin, _site).ConfigureAwait(true)).StatusCode);
        var disabled = await VisitAsync("/no-such-page").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        Assert.Equal(unknownBody, await disabled.Content.ReadAsStringAsync().ConfigureAwait(true));
    }

    private Task<HttpResponseMessage> VisitAsync(string path) =>
        _app.CreateClientForHost(ClientHost).GetAsync(new Uri(path, UriKind.Relative));

    private async Task<string> BodyAsync() =>
        await (await VisitAsync("/no-such-page").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false);
}
