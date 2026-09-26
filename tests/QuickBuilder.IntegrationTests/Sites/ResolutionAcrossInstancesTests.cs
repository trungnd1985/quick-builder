using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 FR-012, SC-005, research S-01: a change made through one instance is what every
/// other instance serves from the very next request.
/// </summary>
/// <remarks>
/// Two application instances over one database: what two containers behind a load
/// balancer look like. Each instance is warmed first, so a cache that survived the change
/// — the defect 001's exception E-4 described for host names — would be caught here.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ResolutionAcrossInstancesTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _first = null!;
    private QuickBuilderApp _second = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        var connection = await database.CreateDatabaseAsync("resolution-instances").ConfigureAwait(false);
        _first = new QuickBuilderApp(connection);
        _second = new QuickBuilderApp(connection);
        _ = _first.Services;
        _ = _second.Services;
        _admin = await _first.CreateAdminClientAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _first.Dispose();
        _second.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Adding_and_removing_a_host_name_on_one_instance_is_seen_by_the_other_at_the_next_request()
    {
        var site = (await SitesApi.CreateOkAsync(_admin, "Moving", ["moving.test"]).ConfigureAwait(true)).GetProperty("id").GetGuid();

        // Warm the second instance with a miss for the name about to be added.
        Assert.Equal(HttpStatusCode.NotFound, await StatusOnSecondAsync("new.moving.test").ConfigureAwait(true));

        Assert.Equal(HttpStatusCode.Created, (await SitesApi.AddHostNameAsync(_admin, site, "new.moving.test").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await StatusOnSecondAsync("new.moving.test").ConfigureAwait(true));

        Assert.Equal(HttpStatusCode.NoContent, (await SitesApi.RemoveHostNameAsync(_admin, site, "new.moving.test").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await StatusOnSecondAsync("new.moving.test").ConfigureAwait(true));
    }

    [Fact]
    public async Task A_new_primary_is_the_base_url_every_instance_reports()
    {
        var created = await SitesApi.CreateOkAsync(_admin, "Renamed", ["old-primary.test", "new-primary.test"]).ConfigureAwait(true);
        var site = created.GetProperty("id").GetGuid();
        var secondAdmin = await IdentitySeed.CreateAdminClientAsync(_second).ConfigureAwait(true);
        Assert.Equal("https://old-primary.test/", (await SitesApi.GetOkAsync(secondAdmin, site).ConfigureAwait(true)).GetProperty("baseUrl").GetString());

        await SitesApi.PutSettingsAsync(_admin, site, "Renamed", "https", "en", "new-primary.test", created.GetProperty("version").GetUInt32()).ConfigureAwait(true);

        Assert.Equal("https://new-primary.test/", (await SitesApi.GetOkAsync(secondAdmin, site).ConfigureAwait(true)).GetProperty("baseUrl").GetString());
    }

    /// <summary>009 US4 scenarios 1 and 4, SC-002: disabling closes the site everywhere at once.</summary>
    [Fact]
    public async Task Disabling_and_enabling_on_one_instance_is_seen_by_the_other_at_the_next_request()
    {
        var site = (await SitesApi.CreateOkAsync(_admin, "Toggled", ["toggled.test"]).ConfigureAwait(true)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, await StatusOnSecondAsync("toggled.test").ConfigureAwait(true));

        Assert.Equal(HttpStatusCode.OK, (await SitesApi.DisableAsync(_admin, site).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await StatusOnSecondAsync("toggled.test").ConfigureAwait(true));

        Assert.Equal(HttpStatusCode.OK, (await SitesApi.EnableAsync(_admin, site).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await StatusOnSecondAsync("toggled.test").ConfigureAwait(true));
    }

    private async Task<HttpStatusCode> StatusOnSecondAsync(string host) =>
        (await SitesApi.PublicGetAsync(_second, host).ConfigureAwait(false)).StatusCode;
}
