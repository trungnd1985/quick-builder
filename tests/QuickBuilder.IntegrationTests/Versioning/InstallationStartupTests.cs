using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Startup;
using QuickBuilder.Shared.Infrastructure.Startup;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.IntegrationTests.Versioning;

/// <summary>
/// Every successful start records which release migrated the database, and a start that
/// changes nothing writes nothing (019 FR-027, FR-030, SC-007; data-model §1–§2).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class InstallationStartupTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_fresh_database_records_the_running_release()
    {
        var connectionString = await database.CreateDatabaseAsync("version-fresh").ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        var running = app.Services.GetRequiredService<ApplicationVersion>().Version.ToString();
        await AssertReadyAsync(app).ConfigureAwait(true);

        var row = await DatabaseState.ReadVersionAsync(connectionString).ConfigureAwait(true);
        Assert.NotNull(row);
        Assert.Equal(running, row.RecordedVersion);
        Assert.Null(row.UpgradingTo);
    }

    [Fact]
    public async Task Starting_again_writes_nothing()
    {
        var connectionString = await database.CreateDatabaseAsync("version-restart").ConfigureAwait(true);

        using (var first = new QuickBuilderApp(connectionString))
        {
            // A claimed installation: an unclaimed one keeps its setup code, but nothing else
            // about this test depends on setup.
            using var admin = await first.CreateAdminClientAsync().ConfigureAwait(true);
        }

        var before = await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true);

        using (var second = new QuickBuilderApp(connectionString))
        {
            await AssertReadyAsync(second).ConfigureAwait(true);
        }

        var after = await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true);
        Assert.Empty(DatabaseState.Changed(before, after));
    }

    /// <summary>
    /// A database written before 0.1.0 has every module's tables and no <c>platform</c> schema,
    /// which is what the 5f549ae build leaves. Throughout 0.x it upgrades (FR-030, rule 2).
    /// </summary>
    [Fact]
    public async Task A_pre_release_database_is_upgraded_and_recorded()
    {
        var connectionString = await database.CreateDatabaseAsync("version-prerelease").ConfigureAwait(true);

        Guid siteId;
        using (var old = new QuickBuilderApp(connectionString))
        {
            using var admin = await old.CreateAdminClientAsync().ConfigureAwait(true);
            siteId = await CreateSiteAsync(admin, "prerelease.test").ConfigureAwait(true);
        }

        await DatabaseState.ExecuteAsync(connectionString, "DROP SCHEMA platform CASCADE").ConfigureAwait(true);

        using var upgraded = new QuickBuilderApp(connectionString);
        var running = upgraded.Services.GetRequiredService<ApplicationVersion>().Version.ToString();
        await AssertReadyAsync(upgraded).ConfigureAwait(true);

        var row = await DatabaseState.ReadVersionAsync(connectionString).ConfigureAwait(true);
        Assert.NotNull(row);
        Assert.Equal(running, row.RecordedVersion);
        Assert.Null(row.UpgradingTo);
        Assert.Equal(1, await DatabaseState.CountAsync(connectionString, $"SELECT count(*) FROM sites.sites WHERE id = '{siteId}'").ConfigureAwait(true));
    }

    /// <summary>
    /// With automatic migration off, the <c>platform</c> schema is still migrated (the check
    /// needs it), readiness says migrations are pending, and nothing is recorded.
    /// </summary>
    [Fact]
    public async Task With_automatic_migration_off_the_record_waits_for_the_migrate_command()
    {
        var connectionString = await database.CreateDatabaseAsync("version-manual").ConfigureAwait(true);

        using var app = new NoAutoMigrateApp(connectionString);
        var response = await app.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(nameof(StartupPhase.MigrationsPending), report.GetProperty("state").GetString());
        Assert.Equal(1, await DatabaseState.CountAsync(connectionString, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'platform' AND table_name = 'database_version'").ConfigureAwait(true));
        Assert.Null(await DatabaseState.ReadVersionAsync(connectionString).ConfigureAwait(true));
    }

    internal static async Task AssertReadyAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        using var client = app.CreateClient();
        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    internal static async Task<Guid> CreateSiteAsync(HttpClient admin, string hostName)
    {
        var created = await admin
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = hostName, hostNames = new[] { hostName } })
            .ConfigureAwait(false);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        return body.GetProperty("id").GetGuid();
    }
}
