using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Startup;
using QuickBuilder.Shared.Infrastructure.Commands;

namespace QuickBuilder.IntegrationTests.Versioning;

/// <summary>
/// What a refused start serves, and that it starts nothing of QuickBuilder's own
/// (019 contracts/installation-version.md §3, research RL-08).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class RefusalHostTests(DatabaseFixture database)
{
    /// <summary>A database a newer minor migrated: every start of this build is refused.</summary>
    internal static async Task<string> RefusedDatabaseAsync(DatabaseFixture database, string label)
    {
        var connectionString = await database.CreateDatabaseAsync(label).ConfigureAwait(false);
        using (var first = new QuickBuilderApp(connectionString))
        {
            await InstallationStartupTests.AssertReadyAsync(first).ConfigureAwait(false);
        }

        await DatabaseState.WriteVersionAsync(connectionString, "0.9.0").ConfigureAwait(false);
        return connectionString;
    }

    [Fact]
    public async Task The_probes_answer_and_everything_else_is_unavailable()
    {
        using var app = new QuickBuilderApp(await RefusedDatabaseAsync(database, "refusal-paths").ConfigureAwait(true));
        using var client = app.CreateClient();

        using (var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative)).ConfigureAwait(true))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        using (var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            using var report = JsonDocument.Parse(await ready.Content.ReadAsStringAsync().ConfigureAwait(true));
            Assert.Equal("Unhealthy", report.RootElement.GetProperty("status").GetString());
            Assert.Equal("Incompatible", report.RootElement.GetProperty("state").GetString());
            Assert.Equal(0, report.RootElement.GetProperty("checks").GetArrayLength());
            Assert.DoesNotMatch(@"\d+\.\d+", report.RootElement.GetProperty("detail").GetString()!);
        }

        foreach (var path in new[] { "/", "/admin/", "/api/v1/sites", "/api/v1/identity/status" })
        {
            using var other = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
            Assert.Equal("text/plain", other.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", other.Headers.CacheControl?.ToString());
            Assert.DoesNotMatch(@"\d+\.\d+", await other.Content.ReadAsStringAsync().ConfigureAwait(true));
        }
    }

    [Fact]
    public async Task Under_a_base_path_the_probes_answer_there()
    {
        using var app = new QuickBuilderApp(
            await RefusedDatabaseAsync(database, "refusal-base").ConfigureAwait(true),
            settings: new Dictionary<string, string> { ["Hosting:BasePath"] = "/qb" });
        using var client = app.CreateClient();

        using var ready = await client.GetAsync(new Uri("/qb/health/ready", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("Incompatible", await ready.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    /// <summary>
    /// With background services allowed, none of QuickBuilder's own is registered: no outbox,
    /// schedule, sweep or backfill runs against a database this release does not understand.
    /// </summary>
    [Fact]
    public async Task No_hosted_service_of_QuickBuilders_own_is_started()
    {
        using var app = new QuickBuilderApp(await RefusedDatabaseAsync(database, "refusal-hosted").ConfigureAwait(true), runBackgroundServices: true);
        _ = app.Server;

        var ours = app.Services.GetServices<IHostedService>()
            .Select(s => s.GetType().FullName!)
            .Where(n => n.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(ours);
    }

    [Fact]
    public async Task A_normal_start_registers_its_hosted_services()
    {
        var connectionString = await database.CreateDatabaseAsync("refusal-control").ConfigureAwait(true);
        using var app = new QuickBuilderApp(connectionString, runBackgroundServices: true);
        _ = app.Server;

        Assert.Contains(app.Services.GetServices<IHostedService>(), s => s.GetType().FullName!.StartsWith("QuickBuilder.", StringComparison.Ordinal));
    }
}

/// <summary>
/// The <c>migrate</c> command refuses with exit code 3, and succeeds with 0
/// (019 contracts/installation-version.md §1).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class MigrateCommandTests(DatabaseFixture database)
{
    [Fact]
    public async Task The_migrate_command_exits_3_on_a_refused_database_and_writes_nothing()
    {
        var connectionString = await RefusalHostTests.RefusedDatabaseAsync(database, "migrate-refused").ConfigureAwait(true);
        var before = await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        var migrate = app.Services.GetServices<IHostCommand>().Single(c => c.Name == "migrate");

        Assert.Equal(3, await migrate.RunAsync([], app.Services, CancellationToken.None).ConfigureAwait(true));
        Assert.Contains(app.Logs.Events, e => e.Level == Serilog.Events.LogEventLevel.Fatal && LogCapture.Render(e).Contains("cannot run on it", StringComparison.Ordinal));
        Assert.Empty(DatabaseState.Changed(before, await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true)));
    }

    [Fact]
    public async Task The_migrate_command_exits_0_and_twice_writes_nothing_the_second_time()
    {
        var connectionString = await database.CreateDatabaseAsync("migrate-ok").ConfigureAwait(true);
        using var app = new NoAutoMigrateApp(connectionString);
        var migrate = app.Services.GetServices<IHostCommand>().Single(c => c.Name == "migrate");

        Assert.Equal(0, await migrate.RunAsync([], app.Services, CancellationToken.None).ConfigureAwait(true));
        var before = await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true);
        Assert.Equal(0, await migrate.RunAsync([], app.Services, CancellationToken.None).ConfigureAwait(true));
        Assert.Empty(DatabaseState.Changed(before, await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true)));
    }
}
