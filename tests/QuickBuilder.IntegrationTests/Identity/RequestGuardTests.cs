using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Requests that did not come from the admin application are refused, and credentials only
/// travel over HTTPS (spec FR-018, US2 scenario 8; research R-04, R-11).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RequestGuardTests(DatabaseFixture database) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("guard").ConfigureAwait(false);
        _app = new QuickBuilderApp(_connectionString);
        _ = _app.Services;
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_state_change_without_the_app_header_is_refused()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        client.DefaultRequestHeaders.Remove("X-QuickBuilder-Request");

        var response = await CreateSiteAsync(client, "noheader.test").ConfigureAwait(true);

        await AssertRejectedAsync(response).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_state_change_from_a_foreign_origin_is_refused()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");

        await AssertRejectedAsync(await CreateSiteAsync(client, "foreign.test").ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_cross_site_fetch_is_refused()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");

        await AssertRejectedAsync(await CreateSiteAsync(client, "crosssite.test").ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_same_origin_request_from_the_app_is_accepted()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

        Assert.Equal(HttpStatusCode.Created, (await CreateSiteAsync(client, "sameorigin.test").ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Reads_need_no_app_header()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        client.DefaultRequestHeaders.Remove("X-QuickBuilder-Request");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Sign_in_over_plain_http_from_another_machine_is_refused()
    {
        var response = await SignInHelpers.SignInAsync(
            SetupHelpers.ClientFrom(_app, "203.0.113.77", https: false),
            "anyone@example.test",
            IdentitySeed.Password).ConfigureAwait(true);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("identity.https_required", problem.GetProperty("errorCode").GetString());
    }

    /// <summary>A link token is a credential too: checking one needs HTTPS as well.</summary>
    [Fact]
    public async Task Checking_a_link_over_plain_http_from_another_machine_is_refused()
    {
        var response = await UsersApi.CheckLinkAsync(
            SetupHelpers.ClientFrom(_app, "203.0.113.78", https: false),
            "any-token").ConfigureAwait(true);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("identity.https_required", problem.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// Behind a TLS proxy the app sees plain HTTP from the proxy. Once the proxy is listed,
    /// its forwarded scheme is trusted and sign-in works (001 D-12, research R-04).
    /// </summary>
    [Fact]
    public async Task Sign_in_through_a_listed_https_proxy_is_accepted()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "proxied@example.test").ConfigureAwait(true);

        using var behindProxy = new ProxiedApp(_connectionString, proxy: "10.0.0.5");
        var client = SetupHelpers.ClientFrom(behindProxy, "10.0.0.5", https: false);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.23");

        var response = await SignInHelpers.SignInAsync(client, "proxied@example.test", IdentitySeed.Password).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> CreateSiteAsync(HttpClient client, string host) =>
        client.PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = host, hostNames = new[] { host } });

    private static async Task AssertRejectedAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("identity.request_rejected", problem.GetProperty("errorCode").GetString());
    }

    /// <summary>The application configured to trust one reverse proxy.</summary>
    private sealed class ProxiedApp(string connectionString, string proxy) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Outbox:Enabled", "false");
            builder.UseSetting("Hosting:KnownProxies:0", proxy);
            IdentitySeed.UseTestRemoteAddress(builder);
        }
    }
}
