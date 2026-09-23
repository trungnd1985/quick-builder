using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Setup mode: until the installation is claimed, the admin API answers "setup required"
/// and nothing else (spec FR-001, FR-006; US1 scenario 1).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SetupGateTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly string[] SetupOperations =
    [
        "GET /api/v1/identity/status",
        "POST /api/v1/identity/setup",
    ];

    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("gate").ConfigureAwait(false));
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Every admin endpoint the application actually has — enumerated, not listed by hand —
    /// answers 428 while unclaimed, except the setup wizard's own two.
    /// </summary>
    [Fact]
    public async Task Every_other_admin_operation_answers_setup_required()
    {
        var client = _app.CreateAnonymousClient();
        var endpoints = EndpointCatalog.Under(_app, "/api")
            .Where(e => !SetupOperations.Contains($"{e.Method} /{e.Pattern.TrimStart('/')}", StringComparer.Ordinal))
            .ToList();

        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var response = await EndpointCatalog.CallAsync(client, endpoint).ConfigureAwait(true);
            Assert.True(
                response.StatusCode == (HttpStatusCode)428,
                $"{endpoint} answered {(int)response.StatusCode} during setup mode instead of 428.");

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
            Assert.Equal("identity.setup_required", problem.GetProperty("errorCode").GetString());
        }
    }

    /// <summary>FR-006: health, the public surface and the admin shell are untouched.</summary>
    [Fact]
    public async Task Health_public_surface_and_admin_shell_are_unaffected()
    {
        // FR-006 concerns sites that already exist while setup is pending, so this uses a
        // database holding a site and no identity data.
        using var unclaimed = new QuickBuilderApp(await CreateUnclaimedWithSiteAsync().ConfigureAwait(true));
        var anonymous = unclaimed.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(new Uri("/health/live", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        var publicSite = await unclaimed.CreateClientForHost("existing.test")
            .GetAsync(new Uri("/", UriKind.Relative))
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, publicSite.StatusCode);

        // The shell itself is static and anonymous; whether its assets exist depends on the
        // admin build, so only "not refused" is asserted.
        var shell = await anonymous.GetAsync(new Uri("/admin/", UriKind.Relative)).ConfigureAwait(true);
        Assert.NotEqual((HttpStatusCode)428, shell.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, shell.StatusCode);
    }

    /// <summary>The status endpoint reports setup mode before the claim and not after.</summary>
    [Fact]
    public async Task Status_reports_whether_setup_is_required()
    {
        var client = _app.CreateAnonymousClient();

        var before = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.True(before.GetProperty("setupRequired").GetBoolean());

        var code = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);
        (await SetupHelpers.PostSetupAsync(client, code).ConfigureAwait(true)).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.False(after.GetProperty("setupRequired").GetBoolean());
    }

    /// <summary>A database holding one site and no identity data.</summary>
    private async Task<string> CreateUnclaimedWithSiteAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("gatesite").ConfigureAwait(true);
        await UpgradeFrom001Tests.MigrateSitesOnlyAsync(connectionString).ConfigureAwait(true);
        await UpgradeFrom001Tests.SeedSiteAsync(connectionString, "Existing", "existing.test").ConfigureAwait(true);
        return connectionString;
    }
}
