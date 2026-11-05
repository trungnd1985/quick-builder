using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// Upgrading a database at 023's migrations to 020 (constitution Principle III): the two new
/// tables arrive empty, existing rows and what visitors see are unchanged, and the existing
/// endpoints answer as before; an export then works on the upgraded site.
/// </summary>
/// <remarks>
/// The 023 database is built by the application itself, then Sites and Identity are migrated
/// back to their last 023 migration. Starting the current application on it is the upgrade, as
/// in <c>UpgradeFrom019Tests</c>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom023Tests(DatabaseFixture database)
{
    private const string SitesAt023 = "20261013123025_AddSearchSettings";
    private const string IdentityAt023 = "20260923195028_InitialIdentity";
    private const string Host = "upgrade023.test";

    [Fact]
    public async Task The_new_tables_arrive_empty_and_existing_sites_answer_as_before()
    {
        var connection = await database.CreateDatabaseAsync("upgrade-023");
        Guid site;
        string before;
        using (var app = new QuickBuilderApp(connection))
        {
            using var admin = await app.CreateAdminClientAsync();
            site = await QuickBuilder.IntegrationTests.Media.MediaApi.CreateSiteAsync(admin, "Upgraded", Host);
            var pages = await PagesApi.SeedTreeAsync(admin, site, "home*\nabout");
            foreach (var (path, id) in pages)
            {
                await PublishingApi.SaveAsync(admin, PagesApi.PagePath(site, id), PublishingApi.HeadingDocument($"Heading of {path}"));
                await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(site, id));
            }

            before = await app.CreateClientForHost(Host).GetStringAsync(new Uri("/about", UriKind.Relative));
        }

        var rowsBefore = await RowsAsync(connection);
        await MigrateBackAsync(connection);
        Assert.Null(await ScalarAsync(connection, "SELECT to_regclass('sites.transfer_jobs')::text"));
        Assert.Null(await ScalarAsync(connection, "SELECT to_regclass('identity.historical_authors')::text"));

        // Starting the application is the upgrade: its migrations run before anything else.
        using var upgraded = new QuickBuilderApp(connection);
        _ = upgraded.Services;

        Assert.Equal("0", await ScalarAsync(connection, "SELECT count(*)::text FROM sites.transfer_jobs"));
        Assert.Equal("0", await ScalarAsync(connection, "SELECT count(*)::text FROM identity.historical_authors"));
        Assert.Equal(rowsBefore, await RowsAsync(connection));
        using var upgradedAdmin = await upgraded.CreateAdminClientAsync();

        // Visitors and the existing endpoints see the same site; the site response only gains fields.
        Assert.Equal(before, await upgraded.CreateClientForHost(Host).GetStringAsync(new Uri("/about", UriKind.Relative)));
        var read = await PagesApi.OkAsync(await upgradedAdmin.GetAsync(new Uri($"/api/v1/sites/{site}", UriKind.Relative)));
        Assert.Equal("Upgraded", read.GetProperty("name").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, read.GetProperty("transfer").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.TreeAsync(upgradedAdmin, site)).StatusCode);

        // And the upgraded site exports.
        var (_, archive) = await TransferApi.ExportAsync(upgraded, upgradedAdmin, site);
        Assert.NotEmpty(archive);
    }

    private static async Task MigrateBackAsync(string connection)
    {
        await using (var sites = new SitesDbContext(Options<SitesDbContext>(connection, SitesDbContext.Schema), NoSite.Instance))
        {
            await sites.GetService<IMigrator>().MigrateAsync(SitesAt023);
        }

        await using var identity = new IdentityDbContext(Options<IdentityDbContext>(connection, IdentityDbContext.Schema));
        await identity.GetService<IMigrator>().MigrateAsync(IdentityAt023);
    }

    /// <summary>Row counts of the tables 023 already had in the two migrated schemas.</summary>
    private static async Task<string> RowsAsync(string connection)
    {
        string[] tables = ["sites.sites", "sites.site_host_names", "identity.users", "identity.security_events"];
        var counts = new List<string>();
        foreach (var table in tables)
        {
            counts.Add($"{table}={await ScalarAsync(connection, $"SELECT count(*)::text FROM {table}")}");
        }

        return string.Join(", ", counts);
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
