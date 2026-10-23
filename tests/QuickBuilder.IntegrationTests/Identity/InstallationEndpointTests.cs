using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Versioning;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Administrators see the running release and the database's; nobody else does
/// (019 FR-006, contracts/installation-version.md §4; constitution Principle IX).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InstallationEndpointTests(DatabaseFixture database)
{
    private static readonly Uri Installation = new("/api/v1/identity/installation", UriKind.Relative);

    [Fact]
    public async Task An_administrator_sees_both_versions()
    {
        using var app = new QuickBuilderApp(await database.CreateDatabaseAsync("installation").ConfigureAwait(true));
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
        var running = app.Services.GetRequiredService<ApplicationVersion>();

        var body = await admin.GetFromJsonAsync<JsonElement>(Installation).ConfigureAwait(true);

        Assert.Equal(running.Version.ToString(), body.GetProperty("version").GetString());
        Assert.Equal(running.Revision, body.GetProperty("revision").GetString());
        Assert.Equal(running.Version.ToString(), body.GetProperty("databaseVersion").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("upgradingTo").ValueKind);

        // A -dev build is not a release, so there are no release notes to link to.
        Assert.Equal(running.Version.PreRelease == "dev" ? JsonValueKind.Null : JsonValueKind.String, body.GetProperty("releaseNotesUrl").ValueKind);
    }

    [Theory]
    [InlineData(SystemRole.Designer)]
    [InlineData(SystemRole.Editor)]
    public async Task Site_roles_are_refused(SystemRole role)
    {
        using var app = new QuickBuilderApp(await database.CreateDatabaseAsync("installation-roles").ConfigureAwait(true));
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
        var site = await Media.MediaApi.CreateSiteAsync(admin, "Roles", "installation-roles.test").ConfigureAwait(true);
        var user = await IdentitySeed.SeedUserAsync(app.Services, siteRoles: [(site, role)]).ConfigureAwait(true);
        var client = await IdentitySeed.CreateSignedInClientAsync(app, user.Id).ConfigureAwait(true);

        using var response = await client.GetAsync(Installation).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotMatch(@"\d+\.\d+\.\d+", await response.Content.ReadAsStringAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Anyone_signed_out_is_refused()
    {
        using var app = new QuickBuilderApp(await database.CreateDatabaseAsync("installation-anon").ConfigureAwait(true));
        _ = await app.CreateAdminClientAsync().ConfigureAwait(true);

        using var response = await app.CreateAnonymousClient().GetAsync(Installation).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
