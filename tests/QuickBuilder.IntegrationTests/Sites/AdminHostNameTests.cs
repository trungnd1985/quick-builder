using System.Net;
using Microsoft.Extensions.Options;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 FR-018a, US3 scenario 7, research S-13: with admin host names configured, the
/// administrative surface answers only on them, and on a client domain it is
/// indistinguishable from a page that does not exist.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminHostNameTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string AdminHost = "admin.test";

    private string _connectionString = string.Empty;
    private QuickBuilderApp _app = null!;
    private HttpClient _adminOnAdminHost = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("admin-hosts").ConfigureAwait(false);
        _app = new QuickBuilderApp(
            _connectionString,
            settings: new Dictionary<string, string> { ["Hosting:AdminHostNames:0"] = "ADMIN.test." });
        _adminOnAdminHost = await _app.CreateAdminClientAsync(AdminHost).ConfigureAwait(false);
        await SitesApi.CreateOkAsync(_adminOnAdminHost, "Client", ["client.test"]).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_administrative_surface_answers_on_an_admin_host_name()
    {
        Assert.Equal(HttpStatusCode.OK, (await SitesApi.ListAsync(_adminOnAdminHost).ConfigureAwait(true)).StatusCode);
        // Whether the shell's files exist depends on the admin build, so only "not the public
        // not-found" is asserted for it.
        var shell = await _adminOnAdminHost.GetAsync(new Uri("/admin/", UriKind.Relative)).ConfigureAwait(true);
        Assert.DoesNotContain("public.not_found", await SitesApi.RawAsync(shell).ConfigureAwait(true), StringComparison.Ordinal);
    }

    /// <summary>
    /// The core promise: status, content type and body are exactly those of a missing page,
    /// even for a signed-in administrator, so nothing reveals an admin surface is there.
    /// </summary>
    [Theory]
    [InlineData("/admin/")]
    [InlineData("/admin/sites")]
    [InlineData("/admin/assets/index.js")]
    [InlineData("/api/v1/sites")]
    [InlineData("/API/v1/identity/me")]
    public async Task On_a_client_domain_admin_paths_look_exactly_like_a_missing_page(string path)
    {
        var signedInOnClient = await _app.CreateAdminClientAsync("client.test").ConfigureAwait(true);

        var missing = await signedInOnClient.GetAsync(new Uri("/no-such-page", UriKind.Relative)).ConfigureAwait(true);
        var admin = await signedInOnClient.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(missing.StatusCode, admin.StatusCode);
        Assert.Equal(missing.Content.Headers.ContentType?.ToString(), admin.Content.Headers.ContentType?.ToString());
        Assert.Equal(await SitesApi.RawAsync(missing).ConfigureAwait(true), await SitesApi.RawAsync(admin).ConfigureAwait(true));

        // No admin-only security headers either: they would give the surface away.
        Assert.False(admin.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task A_state_changing_admin_call_on_a_client_domain_never_runs()
    {
        var signedInOnClient = await _app.CreateAdminClientAsync("client.test").ConfigureAwait(true);

        var response = await SitesApi.CreateAsync(signedInOnClient, "Sneaky", ["sneaky.test"]).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var list = await SitesApi.BodyAsync(await SitesApi.ListAsync(_adminOnAdminHost).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.DoesNotContain(list.EnumerateArray(), s => s.GetProperty("name").GetString() == "Sneaky");
    }

    [Fact]
    public async Task On_an_unknown_host_admin_paths_get_the_unknown_site_response()
    {
        var response = await SitesApi.PublicGetAsync(_app, "nobody.test", "/admin/").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("sites.not_resolved", await SitesApi.RawAsync(response).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("client.test")]
    [InlineData("nobody.test")]
    [InlineData(AdminHost)]
    public async Task Health_answers_on_every_host(string host)
    {
        var response = await SitesApi.PublicGetAsync(_app, host, "/health/live").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The admin surface keeps its own 404 for its own unknown paths.</summary>
    [Fact]
    public async Task An_unknown_api_path_on_an_admin_host_keeps_the_admin_surfaces_answer()
    {
        var response = await _adminOnAdminHost.GetAsync(new Uri("/api/v1/no-such-endpoint", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("public.not_found", await SitesApi.RawAsync(response).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_admin_host_names_the_administrative_surface_answers_everywhere()
    {
        using var open = new QuickBuilderApp(await database.CreateDatabaseAsync("admin-hosts-open").ConfigureAwait(true));
        var admin = await open.CreateAdminClientAsync("any.test").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await SitesApi.ListAsync(admin).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public void An_admin_host_name_that_a_site_holds_stops_startup_naming_the_setting()
    {
        using var conflicting = new QuickBuilderApp(
            _connectionString,
            settings: new Dictionary<string, string> { ["Hosting:AdminHostNames:0"] = "Client.test" });

        var failure = Assert.ThrowsAny<Exception>(() => conflicting.Services);

        var message = Innermost<OptionsValidationException>(failure).Message;
        Assert.Contains("Hosting:AdminHostNames", message, StringComparison.Ordinal);
        Assert.Contains("client.test", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("admin.test:8080")]
    [InlineData("https://admin.test")]
    [InlineData("*.admin.test")]
    public async Task An_invalid_admin_host_name_stops_startup_naming_the_setting(string value)
    {
        using var invalid = new QuickBuilderApp(
            await database.CreateDatabaseAsync("admin-hosts-invalid").ConfigureAwait(true),
            settings: new Dictionary<string, string> { ["Hosting:AdminHostNames:0"] = value });

        var failure = Assert.ThrowsAny<Exception>(() => invalid.Services);

        Assert.Contains("Hosting:AdminHostNames", Innermost<OptionsValidationException>(failure).Message, StringComparison.Ordinal);
    }

    private static T Innermost<T>(Exception failure)
        where T : Exception
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.OfType<T>().FirstOrDefault() is { } inner)
            {
                return inner;
            }
        }

        throw new Xunit.Sdk.XunitException($"Startup failed, but not with {typeof(T).Name}: {failure}");
    }
}
