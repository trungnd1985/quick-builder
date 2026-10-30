using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// Upgrading a database at 019's migrations (release 0.1.0) to 023 (constitution Principle III):
/// items get a draft revision equal to their revision, the new tables appear, and requests from
/// an admin of before 023 (no draft revision, no editing session) keep working.
/// </summary>
/// <remarks>
/// The 019 database is built by the application itself, then the three changed schemas are
/// migrated back to their last 019 migration. Starting the current application on it is the
/// upgrade, as in <c>UpgradeFrom014Tests</c>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom019Tests(DatabaseFixture database)
{
    private const string CmsAt019 = "20261013123050_AddScheduleRedirect";
    private const string PagesAt019 = "20261014171024_AddMenus";
    private const string EditorAt019 = "20260929141629_AddDesignPublishing";

    [Fact]
    public async Task Items_get_their_draft_revision_and_old_admin_requests_still_save()
    {
        var connection = await database.CreateDatabaseAsync("upgrade-019");
        var (site, posts, item, page, schemaRevision) = await SeedAsync(connection);

        await MigrateBackAsync(connection);
        Assert.Null(await ScalarAsync(connection, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'cms' AND table_name = 'items' AND column_name = 'draft_revision'"));
        Assert.Null(await ScalarAsync(connection, "SELECT to_regclass('editor.editing_sessions')::text"));
        var revision = await ScalarAsync(connection, $"SELECT revision::text FROM cms.items WHERE id = '{item}'");

        using var app = new QuickBuilderApp(connection);
        using var admin = await app.CreateAdminClientAsync();

        Assert.Equal(revision, await ScalarAsync(connection, $"SELECT draft_revision::text FROM cms.items WHERE id = '{item}'"));
        Assert.Equal("member", await ScalarAsync(connection, $"SELECT draft_change_kind FROM cms.items WHERE id = '{item}'"));
        Assert.Equal("t", await ScalarAsync(connection, $"SELECT (draft_changed_at_utc = updated_at_utc)::text::char FROM cms.items WHERE id = '{item}'"));
        Assert.NotNull(await ScalarAsync(connection, "SELECT to_regclass('editor.editing_sessions')::text"));
        Assert.NotNull(await ScalarAsync(connection, "SELECT to_regclass('editor.editing_takeovers')::text"));

        // An admin of before 023: the item revision, no draft revision, no session header.
        var stored = await ItemsApi.GetOkAsync(admin, site, posts, item);
        await PagesApi.OkAsync(await ItemsApi.SaveAsync(admin, site, posts, item,
            ItemsApi.Draft(stored.GetProperty("slug").GetString()!, new JsonObject { ["f1"] = "zz-canary-old-admin" }, schemaRevision, ItemsApi.Revision(stored))));
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(admin, PagesApi.PagePath(site, page), PagesApi.DocumentWithText("zz-canary-old-admin")));
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.DraftAsync(admin, PagesApi.PagePath(site, page))).StatusCode);
    }

    private static async Task MigrateBackAsync(string connection)
    {
        await using (var cms = new CmsDbContext(Options<CmsDbContext>(connection, CmsDbContext.Schema), NoSite.Instance))
        {
            await cms.GetService<IMigrator>().MigrateAsync(CmsAt019);
        }

        await using (var pages = new PagesDbContext(Options<PagesDbContext>(connection, PagesDbContext.Schema), NoSite.Instance))
        {
            await pages.GetService<IMigrator>().MigrateAsync(PagesAt019);
        }

        await using var editor = new EditorDbContext(Options<EditorDbContext>(connection, EditorDbContext.Schema), NoSite.Instance);
        await editor.GetService<IMigrator>().MigrateAsync(EditorAt019);
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
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    /// <summary>A site with a page and a post, published once and saved again, on the given database.</summary>
    private static async Task<(Guid Site, Guid Posts, Guid Item, Guid Page, int SchemaRevision)> SeedAsync(string connection)
    {
        using var app = new QuickBuilderApp(connection);
        using var admin = await app.CreateAdminClientAsync();
        var site = await QuickBuilder.IntegrationTests.Media.MediaApi.CreateSiteAsync(admin, "Upgraded", "upgrade-019.test");
        var created = await CmsApi.CreateOkAsync(admin, site, "posts");
        var schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 })) };
        var posts = await CmsApi.SaveOkAsync(admin, site, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, schema));
        var postsId = posts.GetProperty("id").GetGuid();
        var schemaRevision = posts.GetProperty("schemaRevision").GetInt32();

        var item = await ItemsApi.CreateOkAsync(admin, site, postsId, ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-1" }, schemaRevision));
        item = await ItemsApi.PublishOkAsync(admin, site, postsId, ItemsApi.Id(item), ItemsApi.Revision(item));
        await ItemsApi.SaveOkAsync(admin, site, postsId, ItemsApi.Id(item), ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-2" }, schemaRevision, ItemsApi.Revision(item)));

        var page = (await PagesApi.CreateOkAsync(admin, site, "pricing")).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(admin, PagesApi.PagePath(site, page), PagesApi.DocumentWithText("zz-canary-019")));
        return (site, postsId, ItemsApi.Id(item), page, schemaRevision);
    }

    private sealed class NoSite : ISiteContext
    {
        internal static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
