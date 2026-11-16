using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// Upgrading a database from before 021 (constitution Principle III, research TR-21): nothing is
/// in the trash, every address stays, and delete and restore work on the upgraded rows.
/// </summary>
/// <remarks>
/// The older database is built by the application, then Pages, Cms and Media are migrated back
/// to the migration before 021's. Starting the current application on it is the upgrade.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom020Tests(DatabaseFixture database)
{
    private const string PagesBefore = "20261025094748_AddCopyOrigin";
    private const string CmsBefore = "20261025095240_AddItemCopyOrigin";
    private const string MediaBefore = "20260930190425_AddLibraryAndVariants";

    [Fact]
    public async Task Nothing_is_in_the_trash_addresses_stay_and_upgraded_rows_can_be_trashed_and_restored()
    {
        var connection = await database.CreateDatabaseAsync("upgrade-020");
        Guid site;
        Dictionary<string, Guid> pages;
        using (var app = new QuickBuilderApp(connection))
        {
            using var admin = await app.CreateAdminClientAsync();
            site = await QuickBuilder.IntegrationTests.Media.MediaApi.CreateSiteAsync(admin, "Upgraded", "upgrade020.test");
            pages = await PagesApi.SeedTreeAsync(admin, site, "home*\nabout\n  team");
        }

        await MigrateBackAsync(connection);
        Assert.Equal("0", await ScalarAsync(connection, "SELECT count(*)::text FROM information_schema.columns WHERE table_schema = 'pages' AND table_name = 'pages' AND column_name = 'trashed_at_utc'"));

        using var upgraded = new QuickBuilderApp(connection);
        using var upgradedAdmin = await upgraded.CreateAdminClientAsync();

        Assert.Equal("0", await ScalarAsync(connection, "SELECT count(*)::text FROM pages.pages WHERE trashed_at_utc IS NOT NULL"));
        Assert.Equal(["/", "/about", "/about/team"], await PagesApi.PathsAsync(upgradedAdmin, site));

        var team = pages["/about/team"];
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(upgradedAdmin, site, team, 1));
        Assert.Equal(["/", "/about"], await PagesApi.PathsAsync(upgradedAdmin, site));

        var restored = await upgradedAdmin.PostAsJsonAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{team}/restore", UriKind.Relative), new { confirmedAddress = "/about/team" });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(["/", "/about", "/about/team"], await PagesApi.PathsAsync(upgradedAdmin, site));
    }

    private static async Task MigrateBackAsync(string connection)
    {
        await using (var pages = new PagesDbContext(Options<PagesDbContext>(connection, PagesDbContext.Schema), NoSite.Instance))
        {
            await pages.GetService<IMigrator>().MigrateAsync(PagesBefore);
        }

        await using (var cms = new CmsDbContext(Options<CmsDbContext>(connection, CmsDbContext.Schema), NoSite.Instance))
        {
            await cms.GetService<IMigrator>().MigrateAsync(CmsBefore);
        }

        await using var media = new MediaDbContext(Options<MediaDbContext>(connection, MediaDbContext.Schema), NoSite.Instance);
        await media.GetService<IMigrator>().MigrateAsync(MediaBefore);
    }

    private static DbContextOptions<T> Options<T>(string connection, string schema)
        where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(connection, n => n.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;

    private static async Task<string?> ScalarAsync(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        return await command.ExecuteScalarAsync() is { } value and not DBNull ? value.ToString() : null;
    }

    private sealed class NoSite : ISiteContext
    {
        internal static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
