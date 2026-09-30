using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// Upgrading from 011 adds publishing without touching content: drafts and token sets stay
/// byte for byte, nothing is published, and visitors get the not-found page until someone
/// publishes (012 FR-042, constitution Principle III).
/// </summary>
/// <remarks>
/// The snapshot is built rather than stored, as in 009 to 011: every module is migrated to its
/// 011 schema — Pages to <c>InitialPages</c>, Editor to <c>InitialEditor</c> — and a site with
/// a home page, drafts and a saved token set is written in 011's form. Then the current
/// application starts against it, as replacing the image would, and runs its migrations.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom011Tests(DatabaseFixture database)
{
    private const string PagesAt011 = "20260926183212_InitialPages";
    private const string EditorAt011 = "20260928104432_InitialEditor";

    private const string PageDocument = """
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"s","type":"section","props":{},"children":[{"id":"h","type":"heading","props":{"text":"Draft only"},"children":[]}]}]}}
        """;

    [Fact]
    public async Task Content_is_untouched_nothing_is_published_and_visitors_get_the_not_found_page()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-011").ConfigureAwait(true);
        await Build011SnapshotAsync(connectionString).ConfigureAwait(true);
        var drafts = await TextsAsync(connectionString, "SELECT document::text FROM pages.page_drafts ORDER BY 1").ConfigureAwait(true);
        var tokens = await TextsAsync(connectionString, "SELECT document::text FROM editor.design_token_sets ORDER BY 1").ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        _ = app.Services;

        Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM pages.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddPublishing'").ConfigureAwait(true));
        Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM editor.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddDesignPublishing'").ConfigureAwait(true));

        Assert.Equal(drafts, await TextsAsync(connectionString, "SELECT document::text FROM pages.page_drafts ORDER BY 1").ConfigureAwait(true));
        Assert.Equal(tokens, await TextsAsync(connectionString, "SELECT document::text FROM editor.design_token_sets ORDER BY 1").ConfigureAwait(true));

        Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM pages.publications").ConfigureAwait(true));
        Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM pages.published_versions").ConfigureAwait(true));
        Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM editor.design_publications").ConfigureAwait(true));

        var response = await app.CreateClientForHost("client.test").GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Contains(">Page not found</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Draft only", html, StringComparison.Ordinal);
    }

    /// <summary>The 011 schema: a site with a home page draft and a saved token set.</summary>
    private static async Task Build011SnapshotAsync(string connectionString)
    {
        var siteId = Guid.CreateVersion7();
        var pageId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
                .Options);
        await identity.Database.MigrateAsync().ConfigureAwait(false);

        var installation = Installation.CreateUnclaimed("upgrade-011-code", now);
        installation.Claim(now);
        identity.Installation.Add(installation);
        var admin = User.Create("Upgrade Admin", "upgrade-011-admin@example.test", now).Value;
        identity.Users.Add(admin);
        identity.RoleGrants.Add(RoleGrant.Administrator(admin.Id, grantedByUserId: null, now));
        await identity.SaveChangesAsync().ConfigureAwait(false);
        var actor = admin.Id;

        await using (var sites = new SitesDbContext(
            new DbContextOptionsBuilder<SitesDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", SitesDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await sites.Database.MigrateAsync().ConfigureAwait(false);
            await sites.Database.ExecuteSqlAsync($"""
                INSERT INTO sites.sites (id, name, scheme, default_language, status, primary_host_name, created_at_utc, updated_at_utc)
                VALUES ({siteId}, 'Client', 'https', 'en', 'enabled', 'client.test', {now}, {now});
                INSERT INTO sites.site_host_names (id, site_id, host_name, created_at_utc)
                VALUES ({Guid.CreateVersion7()}, {siteId}, 'client.test', {now});
                """).ConfigureAwait(false);
        }

        await using (var media = new MediaDbContext(
            new DbContextOptionsBuilder<MediaDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await media.Database.MigrateAsync().ConfigureAwait(false);
        }

        await using (var pages = new PagesDbContext(
            new DbContextOptionsBuilder<PagesDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PagesDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await pages.GetService<IMigrator>().MigrateAsync(PagesAt011).ConfigureAwait(false);
            await pages.Database.ExecuteSqlAsync($"""
                INSERT INTO pages.page_trees (site_id, page_count, updated_at_utc) VALUES ({siteId}, 1, {now});
                INSERT INTO pages.pages (id, site_id, parent_id, position, title, slug, path, depth, is_home, version, created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES ({pageId}, {siteId}, NULL, 0, 'Home', 'home', '/', 1, TRUE, 1, {now}, {actor}, {now}, {actor});
                INSERT INTO pages.page_drafts (page_id, site_id, schema_version, document, updated_at_utc, updated_by)
                VALUES ({pageId}, {siteId}, 1, CAST({PageDocument} AS jsonb), {now}, {actor});
                """).ConfigureAwait(false);
        }

        await using (var editor = new EditorDbContext(
            new DbContextOptionsBuilder<EditorDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", EditorDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await editor.GetService<IMigrator>().MigrateAsync(EditorAt011).ConfigureAwait(false);
            var document = EditorApi.DefaultDocument.Replace("#2563eb", "#16a34a", StringComparison.OrdinalIgnoreCase);
            await editor.Database.ExecuteSqlAsync($"""
                INSERT INTO editor.design_token_sets (site_id, schema_version, document, updated_at_utc, updated_by)
                VALUES ({siteId}, 1, CAST({document} AS jsonb), {now}, {actor});
                """).ConfigureAwait(false);
        }

        // 011 had no publishing: none of its tables may exist before the upgrade.
        Assert.Equal(0, await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('publications', 'design_publications')").ConfigureAwait(false));
    }

    private static async Task<List<string>> TextsAsync(string connectionString, string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        var texts = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            texts.Add(reader.GetString(0));
        }

        return texts;
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    private sealed class NoSite : ISiteContext
    {
        public static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
