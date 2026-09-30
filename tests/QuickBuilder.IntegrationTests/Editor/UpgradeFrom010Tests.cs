using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// Upgrading from 010 adds the Editor schema, keeps every stored draft exactly as it was and
/// still valid, and gives every existing site the default design tokens (011 FR-043,
/// constitution Principle III).
/// </summary>
/// <remarks>
/// The snapshot is built rather than stored, as in 009 and 010: every module 010 shipped is
/// migrated to its 010 schema — no <c>editor</c> schema — and a page tree with drafts is
/// written in 010's form. Then the current application starts against it, as replacing the
/// image would, and runs its migrations.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom010Tests(DatabaseFixture database)
{
    private const string PageDocument = """
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
          {"id":"header","type":"component-ref","props":{"componentId":"%HEADER%"},"children":[]},
          {"id":"footer","type":"component-ref","props":{"componentId":"%FOOTER%"},"children":[]}]}}
        """;

    private const string EmptyDocument = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[]}}""";

    [Fact]
    public async Task Drafts_are_untouched_and_valid_and_sites_read_the_default_tokens()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-010").ConfigureAwait(true);
        var snapshot = await Build010SnapshotAsync(connectionString).ConfigureAwait(true);
        var before = await DraftTextsAsync(connectionString).ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        var admin = await IdentitySeed.CreateSignedInClientAsync(app, snapshot.AdminId).ConfigureAwait(true);

        // The Editor schema exists, with its own history, and holds nothing yet: defaults are
        // lazy (research ED-14), so the upgrade writes no data.
        // Later releases add their own migrations (012: design publishing); what matters here is
        // that 011's is applied.
        Assert.Equal(1, await CountAsync(
            connectionString,
            "SELECT count(*) FROM editor.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_InitialEditor'").ConfigureAwait(true));
        Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM editor.design_token_sets").ConfigureAwait(true));

        // Every stored draft is exactly as 010 left it.
        Assert.Equal(before, await DraftTextsAsync(connectionString).ConfigureAwait(true));

        // And still valid: each reads back and saves again unchanged under the extended schema.
        var pagePath = PagesApi.PagePath(snapshot.SiteId, snapshot.PageId);
        var draft = await PagesApi.DraftOkAsync(admin, pagePath).ConfigureAwait(true);
        var resaved = await PagesApi.SaveDraftAsync(
            admin, pagePath, draft.GetProperty("document").GetRawText(), draft.GetProperty("version").GetUInt32()).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, resaved.StatusCode);

        var componentPath = PagesApi.ComponentPath(snapshot.SiteId, snapshot.HeaderId);
        var componentDraft = await PagesApi.DraftOkAsync(admin, componentPath).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.SaveDraftAsync(
            admin, componentPath, componentDraft.GetProperty("document").GetRawText(), componentDraft.GetProperty("version").GetUInt32()).ConfigureAwait(true)).StatusCode);

        // The existing site reads the defaults.
        var tokens = await EditorApi.GetOkAsync(admin, snapshot.SiteId).ConfigureAwait(true);
        Assert.Equal((0u, true), (tokens.GetProperty("version").GetUInt32(), tokens.GetProperty("isDefault").GetBoolean()));
        using var defaults = JsonDocument.Parse(EditorApi.DefaultDocument);
        Assert.True(JsonElement.DeepEquals(defaults.RootElement, tokens.GetProperty("document")));
    }

    private sealed record Snapshot(Guid SiteId, Guid AdminId, Guid PageId, Guid HeaderId);

    /// <summary>The 010 schema: a site with a home page whose draft references two components.</summary>
    private static async Task<Snapshot> Build010SnapshotAsync(string connectionString)
    {
        var siteId = Guid.CreateVersion7();
        var pageId = Guid.CreateVersion7();
        var headerId = Guid.CreateVersion7();
        var footerId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
                .Options);
        await identity.Database.MigrateAsync().ConfigureAwait(false);

        var installation = Installation.CreateUnclaimed("upgrade-010-code", now);
        installation.Claim(now);
        identity.Installation.Add(installation);
        var admin = User.Create("Upgrade Admin", "upgrade-010-admin@example.test", now).Value;
        identity.Users.Add(admin);
        identity.RoleGrants.Add(RoleGrant.Administrator(admin.Id, grantedByUserId: null, now));
        await identity.SaveChangesAsync().ConfigureAwait(false);

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
            await pages.Database.MigrateAsync().ConfigureAwait(false);

            var pageDocument = PageDocument
                .Replace("%HEADER%", headerId.ToString(), StringComparison.Ordinal)
                .Replace("%FOOTER%", footerId.ToString(), StringComparison.Ordinal);
            var actor = admin.Id;

            await pages.Database.ExecuteSqlAsync($"""
                INSERT INTO pages.page_trees (site_id, page_count, updated_at_utc) VALUES ({siteId}, 1, {now});
                INSERT INTO pages.pages (id, site_id, parent_id, position, title, slug, path, depth, is_home, version, created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES ({pageId}, {siteId}, NULL, 0, 'Home', 'home', '/', 1, TRUE, 1, {now}, {actor}, {now}, {actor});
                INSERT INTO pages.page_drafts (page_id, site_id, schema_version, document, updated_at_utc, updated_by)
                VALUES ({pageId}, {siteId}, 1, CAST({pageDocument} AS jsonb), {now}, {actor});
                INSERT INTO pages.components (id, site_id, name, created_at_utc, created_by, updated_at_utc, updated_by)
                VALUES ({headerId}, {siteId}, 'Header', {now}, {actor}, {now}, {actor}),
                       ({footerId}, {siteId}, 'Footer', {now}, {actor}, {now}, {actor});
                INSERT INTO pages.component_drafts (component_id, site_id, schema_version, document, updated_at_utc, updated_by)
                VALUES ({headerId}, {siteId}, 1, CAST({EmptyDocument} AS jsonb), {now}, {actor}),
                       ({footerId}, {siteId}, 1, CAST({EmptyDocument} AS jsonb), {now}, {actor});
                INSERT INTO pages.component_usages (page_id, component_id, site_id)
                VALUES ({pageId}, {headerId}, {siteId}), ({pageId}, {footerId}, {siteId});
                """).ConfigureAwait(false);
        }

        // 010 had no editor schema: nothing of it may exist before the upgrade.
        Assert.Equal(0, await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'editor'").ConfigureAwait(false));

        return new Snapshot(siteId, admin.Id, pageId, headerId);
    }

    /// <summary>Every stored draft's document, as PostgreSQL prints it, in a fixed order.</summary>
    private static async Task<List<string>> DraftTextsAsync(string connectionString)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT document::text FROM pages.page_drafts UNION ALL SELECT document::text FROM pages.component_drafts ORDER BY 1",
            connection);
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
