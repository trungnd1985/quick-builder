using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Upgrading from 035 to 016 changes nothing visitors see: the migrations apply, every existing
/// page renders the same body as before, and the items gain their render stamps and folded text
/// (016 tasks T093; constitution Principle III).
/// </summary>
/// <remarks>
/// The 035 snapshot is built rather than stored, as in 009 to 015: content is written through
/// the API, every page is rendered, then Pages and Cms are migrated down to their 035 schemas
/// (<c>AddMediaReferences</c>, <c>AddItems</c>) with items marked as indexed by 035's indexer.
/// The application then starts against it, as replacing the image would, and runs 016's
/// migrations, and the index backfill that would run at start-up.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom035Tests(DatabaseFixture database)
{
    private const string PagesAt035 = "20260930191035_AddMediaReferences";
    private const string CmsAt035 = "20261007174150_AddItems";

    [Fact]
    public async Task Pages_render_the_same_body_and_items_gain_what_016_derives()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-035").ConfigureAwait(true);
        string[] paths = ["/", "/about", "/news"];
        var before = new Dictionary<string, string>();

        using (var app = new QuickBuilderApp(connectionString))
        {
            using var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
            var site = await Media.MediaApi.CreateSiteAsync(admin, "Upgrade 035", "upgrade035.test").ConfigureAwait(true);
            var pages = await PagesApi.SeedTreeAsync(admin, site, "home*\nabout\nnews").ConfigureAwait(true);
            foreach (var (path, id) in pages)
            {
                var subject = PagesApi.PagePath(site, id);
                await PublishingApi.SaveAsync(admin, subject, PublishingApi.HeadingDocument($"Heading of {path}")).ConfigureAwait(true);
                await PublishingApi.PublishCurrentAsync(admin, subject).ConfigureAwait(true);
            }

            var collection = await CmsApi.CreateOkAsync(admin, site, "notes").ConfigureAwait(true);
            var saved = await CmsApi.SaveOkAsync(admin, site, collection.GetProperty("id").GetGuid(), CmsApi.SaveBody(collection, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 100 })),
            })).ConfigureAwait(true);
            var collectionId = collection.GetProperty("id").GetGuid();
            var item = await ItemsApi.CreateOkAsync(admin, site, collectionId,
                ItemsApi.Draft("first-note", new JsonObject { ["f1"] = "Café note" }, saved.GetProperty("schemaRevision").GetInt32())).ConfigureAwait(true);
            await ItemsApi.PublishOkAsync(admin, site, collectionId, ItemsApi.Id(item), ItemsApi.Revision(item)).ConfigureAwait(true);

            using var visitor = app.CreateClientForHost("upgrade035.test");
            foreach (var path in paths)
            {
                before[path] = await visitor.GetStringAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
            }
        }

        await DowngradeTo035Async(connectionString).ConfigureAwait(true);

        using (var upgraded = new QuickBuilderApp(connectionString))
        {
            _ = upgraded.Services;
            Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM pages.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddTemplates'").ConfigureAwait(true));
            Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM cms.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddRenderStamp'").ConfigureAwait(true));
            Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM cms.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddPublishedText'").ConfigureAwait(true));

            using var visitor = upgraded.CreateClientForHost("upgrade035.test");
            foreach (var path in paths)
            {
                using var response = await visitor.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(before[path], await response.Content.ReadAsStringAsync().ConfigureAwait(true));
            }

            // AddRenderStamp backfills the first publication; the index backfill folds the text.
            Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM cms.items WHERE render_stamp >= 1 AND first_published_at_utc IS NOT NULL").ConfigureAwait(true));
            // Background services do not run in tests: the start-up backfill runs here, as at start.
            Assert.Equal(1, await upgraded.Services.GetRequiredService<ItemIndexBackfillService>().RunOnceAsync(CancellationToken.None).ConfigureAwait(true));
            var folded = await CountAsync(connectionString, "SELECT count(*) FROM cms.items WHERE published_text->>'f1' = 'cafe note'").ConfigureAwait(true);
            Assert.Equal(1, folded);
        }
    }

    /// <summary>Pages and Cms back to their 035 schemas, items as 035's indexer left them.</summary>
    private static async Task DowngradeTo035Async(string connectionString)
    {
        await using (var pages = new PagesDbContext(
            new DbContextOptionsBuilder<PagesDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PagesDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await pages.GetService<IMigrator>().MigrateAsync(PagesAt035).ConfigureAwait(false);
        }

        await using (var cms = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", CmsDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await cms.GetService<IMigrator>().MigrateAsync(CmsAt035).ConfigureAwait(false);
            await cms.Database.ExecuteSqlRawAsync("UPDATE cms.items SET index_version = 1").ConfigureAwait(false);
        }

        // 016's columns and tables are gone, as they were at 035.
        Assert.Equal(0, await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.columns WHERE (table_schema = 'pages' AND column_name = 'template_collection_id') " +
            "OR (table_schema = 'cms' AND column_name IN ('render_stamp', 'published_text'))").ConfigureAwait(false));
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    private sealed class NoSite : QuickBuilder.Shared.Infrastructure.MultiSite.ISiteContext
    {
        public static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
