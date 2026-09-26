using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Upgrading from 009 adds the Pages schema and changes nothing else (010 FR-035, research
/// P-18; constitution Principle III: "upgrade a database snapshot from the previous
/// release").
/// </summary>
/// <remarks>
/// The snapshot is built rather than stored, as in 002, 003 and 009: every module that 009
/// shipped is migrated to its current (009) schema, with no <c>pages</c> schema, and seeded.
/// Then the current application starts against it, as replacing the image would, and runs
/// its migrations.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom009Tests(DatabaseFixture database)
{
    [Fact]
    public async Task The_pages_schema_is_added_and_everything_from_009_still_works()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-009").ConfigureAwait(true);
        var (siteId, adminId, editorId) = await Build009SnapshotAsync(connectionString).ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        var admin = await IdentitySeed.CreateSignedInClientAsync(app, adminId).ConfigureAwait(true);

        // The site, its host name and its settings are untouched.
        var site = await admin.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/sites/{siteId}", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(("enabled", "client.test"), (site.GetProperty("status").GetString(), site.GetProperty("primaryHostName").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClientForHost("client.test").GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        // The grant still applies, and media still serves.
        var editor = await IdentitySeed.CreateSignedInClientAsync(app, editorId).ConfigureAwait(true);
        var uploaded = await MediaApi.UploadOkAsync(editor, siteId).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(app, "client.test", uploaded.PublicPath).ConfigureAwait(true)).StatusCode);

        // The pages schema exists, with its own history, and works for an upgraded site.
        var tables = await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'pages'").ConfigureAwait(true);
        Assert.True(tables >= 8, $"Expected the pages tables, found {tables}.");
        Assert.Equal(1, await CountAsync(
            connectionString,
            "SELECT count(*) FROM pages.\"__EFMigrationsHistory\"").ConfigureAwait(true));

        Assert.Empty(await PagesApi.PathsAsync(admin, siteId).ConfigureAwait(true));
        await PagesApi.CreateOkAsync(admin, siteId, "home", isHome: true).ConfigureAwait(true);
        Assert.Equal(["/"], await PagesApi.PathsAsync(editor, siteId).ConfigureAwait(true));
    }

    /// <summary>The 009 schema: one site with a host name, an Administrator and an Editor on it.</summary>
    private static async Task<(Guid SiteId, Guid AdminId, Guid EditorId)> Build009SnapshotAsync(string connectionString)
    {
        var siteId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

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

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
                .Options);
        await identity.Database.MigrateAsync().ConfigureAwait(false);

        var installation = Installation.CreateUnclaimed("upgrade-009-code", now);
        installation.Claim(now);
        identity.Installation.Add(installation);

        var admin = User.Create("Upgrade Admin", "upgrade-009-admin@example.test", now).Value;
        var editor = User.Create("Upgrade Editor", "upgrade-009-editor@example.test", now).Value;
        identity.Users.AddRange(admin, editor);
        identity.RoleGrants.Add(RoleGrant.Administrator(admin.Id, grantedByUserId: null, now));
        identity.RoleGrants.Add(RoleGrant.ForSite(editor.Id, siteId, SystemRole.Editor, grantedByUserId: null, now));
        await identity.SaveChangesAsync().ConfigureAwait(false);

        // 009 had no pages schema: nothing of it may exist before the upgrade.
        Assert.Equal(0, await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'pages'").ConfigureAwait(false));

        return (siteId, admin.Id, editor.Id);
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
