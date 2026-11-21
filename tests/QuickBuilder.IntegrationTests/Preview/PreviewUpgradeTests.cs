using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// Upgrading a database from before 022 (constitution Principle III): the preview tables and the
/// log column arrive empty, existing history still reads, and links work on upgraded content.
/// </summary>
/// <remarks>
/// The older database is built by the application, then Editor and Pages are migrated back to
/// the migration before 022's. Starting the current application on it is the upgrade. Cms has no
/// migration in 022.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewUpgradeTests(DatabaseFixture database)
{
    private const string EditorBefore = "20261025084458_AddEditingSessions";
    private const string PagesBefore = "20261106012905_AddTrash";

    [Fact]
    public async Task The_upgrade_adds_empty_preview_tables_keeps_history_and_links_work()
    {
        var connection = await database.CreateDatabaseAsync("upgrade-021");
        Guid site;
        Guid page;
        using (var app = new QuickBuilderApp(connection))
        {
            using var admin = await app.CreateAdminClientAsync();
            site = await QuickBuilder.IntegrationTests.Media.MediaApi.CreateSiteAsync(admin, "Upgraded", "upgrade021.test");
            page = (await PagesApi.CreateOkAsync(admin, site, "about")).GetProperty("id").GetGuid();
            await PublishingApi.SaveAsync(admin, PagesApi.PagePath(site, page), PublishingApi.HeadingDocument("Before the upgrade"));
            await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(site, page));
        }

        await MigrateBackAsync(connection);
        Assert.Equal("0", await ScalarAsync(connection, "SELECT count(*)::text FROM information_schema.tables WHERE table_schema = 'editor' AND table_name = 'preview_links'"));

        using var upgraded = new QuickBuilderApp(connection);
        using var upgradedAdmin = await upgraded.CreateAdminClientAsync();

        Assert.Equal("0", await ScalarAsync(connection, "SELECT count(*)::text FROM editor.preview_links"));
        Assert.Equal("0", await ScalarAsync(connection, "SELECT count(*)::text FROM pages.publish_log WHERE expires_at_utc IS NOT NULL"));
        var history = await PublishingApi.HistoryAsync(upgradedAdmin, PagesApi.PagePath(site, page));
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains("\"publish\"", await history.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var link = await PreviewLinksApi.CreateOkAsync(upgradedAdmin, site, "page", page, DateTimeOffset.UtcNow.AddDays(1));
        var (status, html, _) = await PreviewLinksApi.OpenAsync(upgraded, link.Path, "upgrade021.test");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Before the upgrade</h1>", html, StringComparison.Ordinal);
    }

    private static async Task MigrateBackAsync(string connection)
    {
        await using (var editor = new EditorDbContext(Options<EditorDbContext>(connection, EditorDbContext.Schema), NoSite.Instance))
        {
            await editor.GetService<IMigrator>().MigrateAsync(EditorBefore);
        }

        await using var pages = new PagesDbContext(Options<PagesDbContext>(connection, PagesDbContext.Schema), NoSite.Instance);
        await pages.GetService<IMigrator>().MigrateAsync(PagesBefore);
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
