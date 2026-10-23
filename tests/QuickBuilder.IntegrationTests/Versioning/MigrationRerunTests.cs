using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Infrastructure.Migrations;

namespace QuickBuilder.IntegrationTests.Versioning;

/// <summary>
/// Migrations are safe to re-run: on an up-to-date database, the startup path and the
/// <c>migrate</c> command change nothing (019 FR-016, SC-007; constitution Principle II).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class MigrationRerunTests(DatabaseFixture database)
{
    [Fact]
    public async Task Running_migrations_again_writes_nothing_by_either_path()
    {
        var connectionString = await database.CreateDatabaseAsync("rerun").ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        using (var admin = await app.CreateAdminClientAsync().ConfigureAwait(true))
        {
            await InstallationStartupTests.CreateSiteAsync(admin, "rerun.test").ConfigureAwait(true);
        }

        var before = await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true);

        // The startup path, as a restart runs it.
        var runner = app.Services.GetRequiredService<ModuleMigrationRunner>();
        await runner.RunAsync(force: false, CancellationToken.None).ConfigureAwait(true);

        // The command path, as `dotnet QuickBuilder.Host.dll migrate` runs it.
        var migrate = app.Services.GetServices<IHostCommand>().Single(c => c.Name == "migrate");
        Assert.Equal(0, await migrate.RunAsync([], app.Services, CancellationToken.None).ConfigureAwait(true));

        var after = await DatabaseState.FingerprintAsync(connectionString).ConfigureAwait(true);
        Assert.Empty(DatabaseState.Changed(before, after));

        Assert.Contains(
            app.Logs.Events,
            e => e.MessageTemplate.Text.Contains("is already up to date", StringComparison.Ordinal));
    }
}
