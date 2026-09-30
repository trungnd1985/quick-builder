using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 US3 scenario 4, FR-017: under a configured base path, both surfaces work and every
/// address the product generates includes it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BasePathTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;
    private Guid _site;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("base-path").ConfigureAwait(false),
            settings: new Dictionary<string, string> { ["Hosting:BasePath"] = "/cms" });
        _admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
        _site = (await SitesApi.CreateOkAsync(_admin, "Under", ["under.test"]).ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PublicSite.MarkAsync(_admin, _site).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_public_and_administrative_surfaces_answer_under_the_base_path()
    {
        var home = await SitesApi.PublicGetAsync(_app, "under.test", "/cms/").ConfigureAwait(true);
        var sites = await _admin.GetAsync(new Uri("/cms/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(_site, await PublicSite.ResolvedSiteAsync(home).ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, sites.StatusCode);

        // 012: every address a published page writes carries the base path too.
        Assert.Contains("href=\"/cms/_assets/blocks.", await SitesApi.RawAsync(home).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_base_url_includes_the_base_path()
    {
        var site = await SitesApi.GetOkAsync(_admin, _site).ConfigureAwait(true);

        Assert.Equal("https://under.test/cms", site.GetProperty("baseUrl").GetString());
    }

    [Fact]
    public async Task Media_addresses_include_the_base_path_and_serve_under_it()
    {
        var uploaded = await MediaApi.UploadOkAsync(_admin, _site).ConfigureAwait(true);

        var listed = await _admin.GetAsync(new Uri($"/cms{MediaApi.FilesPath(_site)}", UriKind.Relative)).ConfigureAwait(true);
        using var page = JsonDocument.Parse(await listed.Content.ReadAsStringAsync().ConfigureAwait(true));
        var publicPath = page.RootElement.GetProperty("items")[0].GetProperty("publicPath").GetString()!;

        Assert.StartsWith("/cms/_media/", publicPath, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_app, "under.test", publicPath).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(_app, "under.test", $"/cms{uploaded.PublicPath}").ConfigureAwait(true)).StatusCode);
    }
}
