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

    /// <summary>
    /// A database a newer minor migrated, or began to, is refused before anything touches it:
    /// readiness says Incompatible without versions, the log names both, and nothing is written
    /// (data-model §2 rules 4 and 6, SC-006). Rules 3 and 7 depend on the build's floor, which
    /// a test cannot lower; <c>InstallationGateTests</c> covers them.
    /// </summary>
    [Theory]
    [InlineData("0.9.0", null)]
    [InlineData("0.1.0-dev", "0.9.0")]
    public async Task A_database_from_a_newer_minor_is_refused_without_writing(string recorded, string? upgradingTo)
    {
        var connectionString = await database.CreateDatabaseAsync("version-newer").ConfigureAwait(true);
        using (var first = new QuickBuilderApp(connectionString))
        {
            await AssertReadyAsync(first).ConfigureAwait(true);
        }

        await DatabaseState.WriteVersionAsync(connectionString, recorded, upgradingTo).ConfigureAwait(true);
        var before = await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true);

        using (var older = new QuickBuilderApp(connectionString))
        {
            using var client = older.CreateClient();
            var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            Assert.True(
                response.StatusCode == HttpStatusCode.ServiceUnavailable,
                $"{(int)response.StatusCode} {body}\n" + string.Join('\n', older.Logs.Events.Select(e => e.Level + " " + e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture))));
            using var parsed = JsonDocument.Parse(body);
            var report = parsed.RootElement;
            Assert.Equal(nameof(StartupPhase.Incompatible), report.GetProperty("state").GetString());
            Assert.DoesNotMatch(@"\d+\.\d+\.\d+", report.GetProperty("detail").GetString()!);

            var running = older.Services.GetRequiredService<ApplicationVersion>().Version.ToString();
            var critical = Assert.Single(older.Logs.Events, e => e.Level == Serilog.Events.LogEventLevel.Fatal);
            var text = LogCapture.Render(critical);
            Assert.Contains(upgradingTo ?? recorded, text, StringComparison.Ordinal);
            Assert.Contains(running, text, StringComparison.Ordinal);
            Assert.Contains("cannot run on it", text, StringComparison.Ordinal);
            Assert.DoesNotContain(older.Logs.Events, e => e.MessageTemplate.Text.Contains("Applying", StringComparison.Ordinal));
        }

        Assert.Empty(DatabaseState.Changed(before, await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true)));
    }

    /// <summary>An older patch of the same minor runs, and never lowers the record (rule 9, clarification Q1).</summary>
    [Fact]
    public async Task An_older_patch_of_the_same_minor_runs_and_leaves_the_record_alone()
    {
        var connectionString = await database.CreateDatabaseAsync("version-patch").ConfigureAwait(true);
        string running;
        using (var first = new QuickBuilderApp(connectionString))
        {
            running = first.Services.GetRequiredService<ApplicationVersion>().Version.ToString();
            await AssertReadyAsync(first).ConfigureAwait(true);
        }

        var newerPatch = SemanticVersion.Parse(running) is var v ? new SemanticVersion(v.Major, v.Minor, v.Patch + 3).ToString() : running;
        await DatabaseState.WriteVersionAsync(connectionString, newerPatch).ConfigureAwait(true);

        using (var app = new QuickBuilderApp(connectionString))
        {
            await AssertReadyAsync(app).ConfigureAwait(true);
        }

        Assert.Equal(newerPatch, (await DatabaseState.ReadVersionAsync(connectionString).ConfigureAwait(true))!.RecordedVersion);
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
