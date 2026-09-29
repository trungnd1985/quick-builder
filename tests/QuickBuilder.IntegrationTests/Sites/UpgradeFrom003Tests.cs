using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// Upgrading an installation that ran feature 003 (009 FR-029, SC-008; constitution
/// Principle III: "upgrade a database snapshot from the previous release").
/// </summary>
/// <remarks>
/// The snapshot is built rather than stored, as in 002 and 003. The difference here is
/// that the Sites schema itself changes, so the current <c>SitesDbContext</c> model cannot
/// write it: the Sites migrations are applied only up to <c>InitialSites</c> — the schema
/// 003 shipped — and the rows are written with SQL in that shape. Then the current
/// application starts against it, as replacing the image would.
/// <para>
/// This is also the dedicated test for the migration's cross-site data step, which picks
/// every site's primary host name at once.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom003Tests(DatabaseFixture database)
{
    private const string LastSitesMigrationIn003 = "20260922154140_InitialSites";

    [Fact]
    public async Task Every_site_becomes_enabled_https_english_with_its_earliest_host_name_primary()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-003").ConfigureAwait(true);
        var (first, second, adminId, editorId) = await Build003SnapshotAsync(connectionString).ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        var admin = await IdentitySeed.CreateSignedInClientAsync(app, adminId).ConfigureAwait(true);

        foreach (var (siteId, primary) in new[] { (first, "first.test"), (second, "second-a.test") })
        {
            var site = await admin.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/sites/{siteId}", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal("enabled", site.GetProperty("status").GetString());
            Assert.Equal("https", site.GetProperty("scheme").GetString());
            Assert.Equal("en", site.GetProperty("defaultLanguage").GetString());
            Assert.Equal(primary, site.GetProperty("primaryHostName").GetString());
            Assert.Equal(primary, site.GetProperty("hostNames")[0].GetString());
            Assert.Equal($"https://{primary}/", site.GetProperty("baseUrl").GetString());
        }

        // Nothing that was reachable stops being reachable.
        foreach (var host in new[] { "first.test", "www.first.test", "second-a.test", "second-b.test", "second-c.test" })
        {
            var response = await app.CreateClientForHost(host).GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
            Assert.True(await PublicSite.IsResolvedAsync(response).ConfigureAwait(true), $"{host} no longer resolves after the upgrade");
        }

        // Grants survive and still scope: the Editor sees exactly the site they held.
        var editor = await IdentitySeed.CreateSignedInClientAsync(app, editorId).ConfigureAwait(true);
        var editorSites = await editor.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(second, Assert.Single(editorSites.EnumerateArray()).GetProperty("id").GetGuid());

        // Media still works on an upgraded site's public address.
        var uploaded = await MediaApi.UploadOkAsync(editor, second).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(app, "second-b.test", uploaded.PublicPath).ConfigureAwait(true)).StatusCode);

        // And the upgraded site behaves like a new one: its settings can be edited.
        var version = (await admin.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/sites/{second}", UriKind.Relative)).ConfigureAwait(true))
            .GetProperty("version").GetUInt32();
        var edited = await SitesApi.PutSettingsAsync(admin, second, "Second", "https", "de", "second-c.test", version).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
    }

    /// <summary>
    /// The 003 schema: two sites, the second with three host names added at different
    /// times, an Administrator, and an Editor on the second site.
    /// </summary>
    private static async Task<(Guid First, Guid Second, Guid AdminId, Guid EditorId)> Build003SnapshotAsync(string connectionString)
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var t0 = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        await using (var sites = new SitesDbContext(
            new DbContextOptionsBuilder<SitesDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", SitesDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await sites.GetService<IMigrator>().MigrateAsync(LastSitesMigrationIn003).ConfigureAwait(false);

            // The InitialSites shape. Host names are inserted out of creation order, so
            // the migration has to order by time rather than rely on insertion order.
            await sites.Database.ExecuteSqlAsync($"""
                INSERT INTO sites.sites (id, name, created_at_utc, updated_at_utc)
                VALUES ({first}, 'First', {t0}, {t0}), ({second}, 'Second', {t0}, {t0});
                INSERT INTO sites.site_host_names (id, site_id, host_name, created_at_utc) VALUES
                    ({Guid.CreateVersion7()}, {first}, 'www.first.test', {t0.AddMinutes(5)}),
                    ({Guid.CreateVersion7()}, {first}, 'first.test', {t0.AddMinutes(1)}),
                    ({Guid.CreateVersion7()}, {second}, 'second-c.test', {t0.AddMinutes(9)}),
                    ({Guid.CreateVersion7()}, {second}, 'second-a.test', {t0.AddMinutes(2)}),
                    ({Guid.CreateVersion7()}, {second}, 'second-b.test', {t0.AddMinutes(3)});
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

        var now = DateTimeOffset.UtcNow;
        var installation = Installation.CreateUnclaimed("upgrade-003-code", now);
        installation.Claim(now);
        identity.Installation.Add(installation);

        var admin = User.Create("Upgrade Admin", "upgrade-003-admin@example.test", now).Value;
        var editor = User.Create("Upgrade Editor", "upgrade-003-editor@example.test", now).Value;
        identity.Users.AddRange(admin, editor);
        identity.RoleGrants.Add(RoleGrant.Administrator(admin.Id, grantedByUserId: null, now));
        identity.RoleGrants.Add(RoleGrant.ForSite(editor.Id, second, SystemRole.Editor, grantedByUserId: null, now));
        await identity.SaveChangesAsync().ConfigureAwait(false);

        return (first, second, admin.Id, editor.Id);
    }

    private sealed class NoSite : ISiteContext
    {
        public static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
