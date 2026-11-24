using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// #51: the admin shell is built with relative addresses and served with them made absolute
/// under the base path, so the admin loads at <c>{Hosting:BasePath}/admin/</c> and on any deep
/// link below it.
/// </summary>
/// <remarks>
/// Each app serves a stand-in shell from its own web root, so the test does not depend on the
/// admin app having been built into wwwroot.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AdminShellTests(DatabaseFixture database) : IAsyncLifetime
{
    // What `vite build` writes with `base: './'`, cut down.
    private const string Shell = """
        <!doctype html>
        <html lang="en">
          <head>
            <script type="module" crossorigin src="./assets/index.abc.js"></script>
            <link rel="modulepreload" crossorigin href="./assets/request.def.js">
            <link rel="stylesheet" crossorigin href="./assets/index.ghi.css">
          </head>
          <body><a href="#main">Skip</a><div id="app"></div></body>
        </html>
        """;

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "qb-admin-shell-" + Guid.NewGuid().ToString("N"));
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "admin"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "admin", "index.html"), Shell).ConfigureAwait(false);
        _connectionString = await database.CreateDatabaseAsync("admin-shell").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_webRoot, recursive: true);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("", "/admin/")]
    [InlineData("", "/admin/sites/11111111-1111-1111-1111-111111111111/pages")]
    [InlineData("/cms", "/cms/admin/")]
    [InlineData("/cms", "/cms/admin/sites/11111111-1111-1111-1111-111111111111/pages")]
    public async Task The_shell_loads_its_assets_from_under_the_base_path(string basePath, string address)
    {
        using var app = App(basePath);

        var response = await app.CreateAnonymousClient().GetAsync(new Uri(address, UriKind.Relative)).ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"src=\"{basePath}/admin/assets/index.abc.js\"", body, StringComparison.Ordinal);
        Assert.Contains($"href=\"{basePath}/admin/assets/request.def.js\"", body, StringComparison.Ordinal);
        Assert.Contains($"href=\"{basePath}/admin/assets/index.ghi.css\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"./", body, StringComparison.Ordinal);

        // Fragment links stay on the page (why the shell has no <base> element).
        Assert.Contains("href=\"#main\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shell_keeps_the_admin_policy_and_is_never_cached()
    {
        using var app = App("/cms");

        var response = await app.CreateAnonymousClient().GetAsync(new Uri("/cms/admin/sites", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.StartsWith("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    private QuickBuilderApp App(string basePath)
    {
        var settings = new Dictionary<string, string> { ["webroot"] = _webRoot };
        if (basePath.Length > 0)
        {
            settings["Hosting:BasePath"] = basePath;
        }

        return new QuickBuilderApp(_connectionString, settings: settings);
    }
}
