using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Migrations;
using QuickBuilder.Shared.Infrastructure.Startup;
using QuickBuilder.Shared.Infrastructure.Storage;
using Xunit;

namespace QuickBuilder.IntegrationTests.Startup;

/// <summary>
/// Startup, restart and the manual migration path (SC-002, SC-006, FR-023, FR-028).
/// </summary>
/// <remarks>
/// These are the behaviours an operator meets first and judges the product by. Each was
/// verified by hand against the reference deployment during implementation; encoding them
/// is what stops a regression reaching one.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class StartupTests(DatabaseFixture database) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    public async Task InitializeAsync() =>
        _connectionString = await database.CreateDatabaseAsync("startup").ConfigureAwait(true);

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// SC-006: stopping and starting against existing data loses nothing.
    /// </summary>
    /// <remarks>
    /// The property that makes an upgrade safe. A second host over the same database is
    /// what a restart looks like from the data's point of view, and the assertion is that
    /// the site created before it is still there afterwards — with no manual step in
    /// between.
    /// </remarks>
    [Fact]
    public async Task Restarting_against_existing_data_loses_nothing()
    {
        Guid siteId;

        using (var first = new QuickBuilderApp(_connectionString))
        {
            var client = await first.CreateAdminClientAsync().ConfigureAwait(true);
            var created = await client
                .PostAsJsonAsync(
                    new Uri("/api/v1/sites", UriKind.Relative),
                    new { name = "Survivor", hostNames = new[] { "survivor.test" } })
                .ConfigureAwait(true);

            created.EnsureSuccessStatusCode();
            var body = await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
            siteId = body.GetProperty("id").GetGuid();
        }

        using var second = new QuickBuilderApp(_connectionString);
        var restarted = await second.CreateAdminClientAsync().ConfigureAwait(true);

        var ready = await restarted.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);
        var sites = await restarted.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        var payload = await sites.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains(
            payload.EnumerateArray(),
            s => s.GetProperty("id").GetGuid() == siteId);
    }

    /// <summary>
    /// SC-002: a start against an already-migrated database reaches ready quickly.
    /// </summary>
    /// <remarks>
    /// A generous ceiling on purpose. Shared CI runners are slower and noisier than the
    /// reference two-core environment, so a tight assertion would flake without catching
    /// anything real. SC-002's actual 30-second warm budget is measured against the
    /// reference environment in the quickstart run; this guards against an
    /// order-of-magnitude regression, such as migrations being re-scanned every start.
    /// </remarks>
    [Fact]
    public async Task Warm_start_reaches_ready_promptly()
    {
        // First host applies migrations.
        using (var warmUp = new QuickBuilderApp(_connectionString))
        {
            _ = warmUp.Services;
        }

        var started = DateTimeOffset.UtcNow;

        using var app = new QuickBuilderApp(_connectionString);
        var response = await app.CreateClient()
            .GetAsync(new Uri("/health/ready", UriKind.Relative))
            .ConfigureAwait(true);

        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            elapsed < TimeSpan.FromSeconds(60),
            $"Warm start took {elapsed.TotalSeconds:F1}s, which suggests a startup regression.");
    }

    /// <summary>
    /// FR-023: with automatic migration off, readiness says so rather than lying.
    /// </summary>
    /// <remarks>
    /// The state that makes a half-deployed installation visible. Without a distinct
    /// phase, an instance that deliberately skipped migrations is indistinguishable from
    /// one still starting, and an operator has no way to tell whether to wait or to act.
    /// </remarks>
    [Fact]
    public async Task Disabling_automatic_migration_reports_migrations_pending()
    {
        using var app = new NoAutoMigrateApp(_connectionString);

        var response = await app.CreateClient()
            .GetAsync(new Uri("/health/ready", UriKind.Relative))
            .ConfigureAwait(true);

        var report = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            nameof(StartupPhase.MigrationsPending),
            report.GetProperty("state").GetString());

        // The detail tells the operator what to run, rather than only that something is wrong.
        Assert.Contains("migrate", report.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// FR-023: the migrate command then brings the same database up to date.
    /// </summary>
    /// <remarks>
    /// The other half of the opt-out. Skipping migrations is only useful if applying them
    /// deliberately works, so this runs the same code path the <c>migrate</c> command does
    /// and asserts readiness recovers.
    /// </remarks>
    [Fact]
    public async Task The_migrate_command_applies_migrations_when_automatic_is_off()
    {
        using var app = new NoAutoMigrateApp(_connectionString);

        var runner = app.Services.GetRequiredService<ModuleMigrationRunner>();
        await runner.RunAsync(force: true, CancellationToken.None).ConfigureAwait(true);

        var response = await app.CreateClient()
            .GetAsync(new Uri("/health/ready", UriKind.Relative))
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// 003 research M-22: a storage location that stops being usable after startup takes the
    /// instance out of rotation, and putting it right brings it back.
    /// </summary>
    /// <remarks>
    /// Unix only: Windows ACLs are not modelled by <see cref="UnixFileMode"/>. Also skipped
    /// as root, which ignores permission bits entirely.
    /// </remarks>
    [SkippableTheory]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserExecute)]
    [InlineData((UnixFileMode)0)]
    public async Task Readiness_fails_while_storage_is_read_only_or_unreadable(UnixFileMode brokenMode)
    {
        Skip.If(OperatingSystem.IsWindows(), "Unix file modes only.");
        Skip.If(Environment.UserName == "root", "Root ignores permission bits.");

        using var app = new QuickBuilderApp(_connectionString);
        var client = app.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.OK, (await ReadyAsync(client).ConfigureAwait(true)).StatusCode);

        var original = File.GetUnixFileMode(app.StoragePath);
        try
        {
            File.SetUnixFileMode(app.StoragePath, brokenMode);

            var broken = await ReadyAsync(client).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, broken.StatusCode);
            Assert.Equal("Unhealthy", await StorageCheckAsync(broken).ConfigureAwait(true));
        }
        finally
        {
            File.SetUnixFileMode(app.StoragePath, original);
        }

        Assert.Equal(HttpStatusCode.OK, (await ReadyAsync(client).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>003 research M-22: a storage check that hangs counts as unhealthy after 2 seconds.</summary>
    [Fact]
    public async Task Readiness_fails_when_the_storage_check_hangs()
    {
        using var app = new QuickBuilderApp(
            _connectionString,
            configureTestServices: services => services.AddSingleton<IFileStorageHealthCheck, HangingHealthCheck>());

        var started = System.Diagnostics.Stopwatch.StartNew();
        var response = await ReadyAsync(app.CreateAnonymousClient()).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await StorageCheckAsync(response).ConfigureAwait(true));
        Assert.InRange(started.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(10));
    }

    private static Task<HttpResponseMessage> ReadyAsync(HttpClient client) =>
        client.GetAsync(new Uri("/health/ready", UriKind.Relative));

    private static async Task<string?> StorageCheckAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(true));
        return body.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "storage")
            .GetProperty("status").GetString();
    }

    /// <summary>A provider whose health check never answers and ignores cancellation.</summary>
    private sealed class HangingHealthCheck : IFileStorageHealthCheck
    {
        public Task<bool> CheckAsync(CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<bool>().Task;
    }
}

/// <summary>A host with <c>Database:AutoMigrate=false</c>.</summary>
internal sealed class NoAutoMigrateApp(string connectionString) : WebApplicationFactory<Program>
{
    private string StoragePath { get; } = QuickBuilderApp.CreateTemporaryStorage();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            QuickBuilderApp.TryDeleteDirectory(StoragePath);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Database:AutoMigrate", "false");
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("Storage:Local:Path", StoragePath);

        builder.ConfigureServices(services =>
        {
            var hosted = services
                .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                .ToList();

            foreach (var descriptor in hosted)
            {
                services.Remove(descriptor);
            }
        });
    }
}
