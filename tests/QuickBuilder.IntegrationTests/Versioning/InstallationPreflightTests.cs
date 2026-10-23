using Microsoft.Extensions.Configuration;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Migrations.Platform;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.IntegrationTests.Versioning;

/// <summary>
/// The check before the host is built answers only when it can decide, and leaves every other
/// failure to the migration runner, which already reports it (019 contracts/installation-version.md §1).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class InstallationPreflightTests(DatabaseFixture database)
{
    private static readonly ApplicationVersion Running = ApplicationVersion.Create("0.2.0", "0.0.0");

    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public async Task Without_a_connection_string_it_does_not_decide()
    {
        Assert.Null(await InstallationPreflight.CheckAsync(Settings(), Running));
    }

    [Fact]
    public async Task A_database_that_never_answers_is_left_to_the_runner()
    {
        var settings = Settings(
            ("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1"),
            ("Database:ConnectionRetryTimeout", "00:00:01"),
            ("Database:ConnectionRetryDelay", "00:00:00.200"));

        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await InstallationPreflight.CheckAsync(settings, Running));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"It gave up after {started.Elapsed}.");
    }

    [Fact]
    public async Task A_fresh_database_passes_and_a_newer_one_is_refused_with_both_versions()
    {
        var connectionString = await database.CreateDatabaseAsync("preflight").ConfigureAwait(true);
        var settings = Settings(("ConnectionStrings:Default", connectionString));

        Assert.Null(await InstallationPreflight.CheckAsync(settings, Running));

        await DatabaseState.WriteVersionAsync(connectionString, "0.3.0").ConfigureAwait(true);
        var refusal = await InstallationPreflight.CheckAsync(settings, Running);

        Assert.NotNull(refusal);
        Assert.Contains("0.3.0", refusal.Value.LogMessage, StringComparison.Ordinal);
        Assert.Contains("0.2.0", refusal.Value.LogMessage, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d+\.\d+\.\d+", refusal.Value.Detail);
    }
}
