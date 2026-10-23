using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.IntegrationTests.Versioning;

/// <summary>
/// An upgrade killed halfway is finished by starting again, without manual repair
/// (019 FR-018, data-model §2 rule 5).
/// </summary>
/// <remarks>
/// Each EF Core migration runs in its own transaction, so a process killed mid-upgrade leaves
/// the last completed migration applied and nothing half-done. The test models that by
/// applying only part of the pending migrations, then starting the application as an operator
/// restarting the container would.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class InterruptedMigrationTests(DatabaseFixture database)
{
    [Fact]
    public async Task Starting_again_finishes_an_upgrade_that_stopped_halfway()
    {
        var connectionString = await database.CreateDatabaseAsync("interrupted").ConfigureAwait(true);

        string running;
        using (var first = new QuickBuilderApp(connectionString))
        {
            running = first.Services.GetRequiredService<ApplicationVersion>().Version.ToString();
            using var admin = await first.CreateAdminClientAsync().ConfigureAwait(true);
            await InstallationStartupTests.CreateSiteAsync(admin, "interrupted.test").ConfigureAwait(true);
        }

        // An older Pages and Cms, an upgrade to this release begun, and only part of it done.
        await using (var pages = Pages(connectionString))
        await using (var cms = Cms(connectionString))
        {
            var pageMigrations = pages.Database.GetMigrations().ToList();
            var cmsMigrations = cms.Database.GetMigrations().ToList();
            Assert.True(pageMigrations.Count >= 4 && cmsMigrations.Count >= 3, "The test needs migrations to undo.");

            await pages.GetService<IMigrator>().MigrateAsync(pageMigrations[^4]).ConfigureAwait(true);
            await cms.GetService<IMigrator>().MigrateAsync(cmsMigrations[^3]).ConfigureAwait(true);

            await DatabaseState.WriteVersionAsync(connectionString, recorded: "0.0.0", upgradingTo: running).ConfigureAwait(true);

            // "Killed" after two more Pages migrations: Cms not started, Pages half done.
            await pages.GetService<IMigrator>().MigrateAsync(pageMigrations[^2]).ConfigureAwait(true);
            Assert.NotEmpty(await pages.Database.GetPendingMigrationsAsync().ConfigureAwait(true));
        }

        using (var restarted = new QuickBuilderApp(connectionString))
        {
            await InstallationStartupTests.AssertReadyAsync(restarted).ConfigureAwait(true);
        }

        await using (var pages = Pages(connectionString))
        await using (var cms = Cms(connectionString))
        {
            Assert.Empty(await pages.Database.GetPendingMigrationsAsync().ConfigureAwait(true));
            Assert.Empty(await cms.Database.GetPendingMigrationsAsync().ConfigureAwait(true));
        }

        var row = await DatabaseState.ReadVersionAsync(connectionString).ConfigureAwait(true);
        Assert.NotNull(row);
        Assert.Equal(running, row.RecordedVersion);
        Assert.Null(row.UpgradingTo);
        Assert.Equal(1, await DatabaseState.CountAsync(connectionString, "SELECT count(*) FROM sites.site_host_names WHERE host_name = 'interrupted.test'").ConfigureAwait(true));
    }

    private static PagesDbContext Pages(string connectionString) =>
        new(
            new DbContextOptionsBuilder<PagesDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PagesDbContext.Schema))
                .Options,
            NoSite.Instance);

    private static CmsDbContext Cms(string connectionString) =>
        new(
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", CmsDbContext.Schema))
                .Options,
            NoSite.Instance);

    private sealed class NoSite : QuickBuilder.Shared.Infrastructure.MultiSite.ISiteContext
    {
        internal static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
