using System.Net;
using Npgsql;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Upgrading from 013 adds an empty Cms schema and changes nothing else (014 CS-21,
/// constitution Principle III).
/// </summary>
/// <remarks>
/// The 013 installation is built by the application itself — a site, a page with a draft,
/// a media file — and then the <c>cms</c> schema is dropped, which is exactly what 013 had:
/// no Cms module at all. Starting the current application against that database is the upgrade.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom013Tests(DatabaseFixture database)
{
    [Fact]
    public async Task The_cms_schema_appears_empty_and_pages_and_media_are_untouched()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-013");
        var storage = QuickBuilderApp.CreateTemporaryStorage();

        try
        {
            Guid siteId;
            using (var before = new QuickBuilderApp(connectionString, storagePath: storage))
            {
                using var admin = await before.CreateAdminClientAsync();
                siteId = await MediaApi.CreateSiteAsync(admin, "Upgraded", "upgrade-013.test");
                await PagesApi.CreateOkAsync(admin, siteId, "home", isHome: true);
                await MediaApi.UploadOkAsync(admin, siteId);
            }

            await ExecuteAsync(connectionString, "DROP SCHEMA cms CASCADE");
            var pagesBefore = await ScalarAsync(connectionString, "SELECT string_agg(id::text || title, ',' ORDER BY id) FROM pages.pages");
            var mediaBefore = await ScalarAsync(connectionString, "SELECT string_agg(id::text || storage_key, ',' ORDER BY id) FROM media.media_files");

            using var app = new QuickBuilderApp(connectionString, storagePath: storage);
            using var client = await app.CreateAdminClientAsync();

            Assert.Equal("1", await ScalarAsync(connectionString, "SELECT count(*)::text FROM cms.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_InitialCms'"));
            Assert.Equal("0", await ScalarAsync(connectionString, "SELECT count(*)::text FROM cms.collections"));
            Assert.Equal(pagesBefore, await ScalarAsync(connectionString, "SELECT string_agg(id::text || title, ',' ORDER BY id) FROM pages.pages"));
            Assert.Equal(mediaBefore, await ScalarAsync(connectionString, "SELECT string_agg(id::text || storage_key, ',' ORDER BY id) FROM media.media_files"));

            var list = await PagesApi.OkAsync(await CmsApi.ListAsync(client, siteId));
            Assert.Equal(0, list.GetProperty("collections").GetArrayLength());
            Assert.Equal(HttpStatusCode.Created, (await CmsApi.CreateAsync(client, siteId, "blog-posts")).StatusCode);
        }
        finally
        {
            QuickBuilderApp.TryDeleteDirectory(storage);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (await command.ExecuteScalarAsync())?.ToString();
    }
}
