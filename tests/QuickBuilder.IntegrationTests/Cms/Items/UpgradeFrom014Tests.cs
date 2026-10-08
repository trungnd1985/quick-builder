using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// Upgrading from 014 gives stored items what 015 needs (research IT-22, constitution Principle III).
/// </summary>
/// <remarks>
/// The 014 installation is built by the application itself, a collection with draft-only and
/// published items, then the Cms schema is migrated back to <c>InitialCms</c>, which is exactly
/// what 014 had. Starting the current application against that database is the upgrade; the
/// start-up backfill is run by hand, because the test host starts no background services.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom014Tests(DatabaseFixture database)
{
    private const string CmsAt014 = "20261007065344_InitialCms";

    [Fact]
    public async Task Stored_items_get_slugs_statuses_versions_and_an_index()
    {
        var connection = await database.CreateDatabaseAsync("upgrade-014");
        var (siteId, collectionId, items) = await Build014Async(connection);

        using var app = new QuickBuilderApp(connection);
        using var client = await app.CreateAdminClientAsync();

        // The migration: placeholder slugs, statuses, and a live version 1 for published items.
        Assert.Equal("4", await ScalarAsync(connection, "SELECT count(*)::text FROM cms.items WHERE slug LIKE 'item-%' AND revision = 1 AND index_version = 0"));
        Assert.Equal("2", await ScalarAsync(connection, "SELECT count(*)::text FROM cms.items WHERE status = 'published' AND live_version = 1 AND next_version_number = 2 AND published_slug = slug"));
        Assert.Equal("2", await ScalarAsync(connection, "SELECT count(*)::text FROM cms.items WHERE status = 'draft' AND live_version IS NULL"));
        Assert.Equal("2", await ScalarAsync(connection, "SELECT count(*)::text FROM cms.item_versions WHERE number = 1 AND kind = 'publish'"));
        Assert.Equal("4", await ScalarAsync(connection, "SELECT count(*)::text FROM cms.item_slugs"));
        Assert.Equal(await ScalarAsync(connection, "SELECT string_agg(revision::text, ',') FROM cms.collections"),
            await ScalarAsync(connection, "SELECT string_agg(schema_revision::text, ',') FROM cms.collections"));

        // The backfill: names, search text, slugs from names (made unique), the derived rows.
        var indexed = await app.Services.GetRequiredService<ItemIndexBackfillService>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(4, indexed);

        await using var scope = app.Services.CreateAsyncScope();
        var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var stored = await cms.ForSite<Item>(siteId).AsNoTracking().Where(i => i.CollectionId == collectionId).OrderBy(i => i.Id).ToListAsync();

        Assert.All(stored, i => Assert.Equal(ItemIndexer.CurrentVersion, i.IndexVersion));
        Assert.Equal(["hello-world", "hello-world-2", Item.PlaceholderSlug(items[2]), "angstrom"], stored.Select(i => i.Slug));
        Assert.Equal("hello-world-2", stored[1].PublishedSlug);
        Assert.Equal(["Hello world", "Hello world", null, "Ångström"], stored.Select(i => i.Name));
        Assert.Equal("angstrom angstrom", stored[3].SearchText);
        Assert.Null(stored[0].Attention);
        Assert.NotNull(stored[2].Attention);
        Assert.Equal(4, await cms.ForSite<ItemSlug>(siteId).CountAsync());

        // A second run has nothing left to do.
        Assert.Equal(0, await app.Services.GetRequiredService<ItemIndexBackfillService>().RunOnceAsync(CancellationToken.None));
    }

    /// <summary>
    /// A 014 installation: a collection with a required title, two draft-only items and two
    /// published ones, two of them sharing a name and one with no title.
    /// </summary>
    private static async Task<(Guid Site, Guid Collection, List<Guid> Items)> Build014Async(string connection)
    {
        Guid siteId;
        Guid collectionId;
        var items = new List<Guid>();

        using (var before = new QuickBuilderApp(connection))
        {
            using var admin = await before.CreateAdminClientAsync();
            siteId = await MediaApi.CreateSiteAsync(admin, "Upgraded", "upgrade-014.test");
            var schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 })) };
            var created = await CmsApi.CreateOkAsync(admin, siteId, "posts");
            collectionId = created.GetProperty("id").GetGuid();
            await CmsApi.SaveOkAsync(admin, siteId, collectionId, CmsApi.SaveBody(created, schema));

            await using var scope = before.Services.CreateAsyncScope();
            var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            foreach (var (title, published) in new[] { ("Hello world", false), ("Hello world", true), ((string?)null, false), ("Ångström", true) })
            {
                var values = Values(title);
                var item = Item.Create(siteId, collectionId, values, published ? Values(title) : null, TimeProvider.System);
                cms.Items.Add(item);
                items.Add(item.Id);
                await Task.Delay(2);
            }

            await cms.SaveChangesAsync();
        }

        await using (var cms = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseNpgsql(connection, n => n.MigrationsHistoryTable("__EFMigrationsHistory", CmsDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await cms.GetService<IMigrator>().MigrateAsync(CmsAt014);
        }

        Assert.Null(await ScalarAsync(connection, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'cms' AND table_name = 'items' AND column_name = 'slug'"));
        items.Sort();
        return (siteId, collectionId, items);
    }

    private static ItemValues Values(string? title) => ItemValues.Read(JsonDocument.Parse(
        title is null
            ? """{ "schemaVersion": 1, "values": {} }"""
            : new JsonObject { ["schemaVersion"] = 1, ["values"] = new JsonObject { ["f1"] = title } }.ToJsonString()));

    private static async Task<string?> ScalarAsync(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private sealed class NoSite : ISiteContext
    {
        internal static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
