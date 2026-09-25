using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Upgrading an installation that ran feature 002 (003 SC-005's database half; constitution
/// Principle III: "upgrade a database snapshot from the previous release").
/// </summary>
/// <remarks>
/// As in 002's <c>UpgradeFrom001Tests</c>, the snapshot is built rather than stored: only
/// the Sites and Identity migrations are applied — the schema 002 shipped — and data is
/// written into it. Then the current application starts against it, as replacing the image
/// would, and must add the media schema without touching anything else.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom002Tests(DatabaseFixture database)
{
    [Fact]
    public async Task An_upgraded_installation_keeps_every_site_account_and_grant_and_gains_empty_libraries()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-002").ConfigureAwait(true);
        var (siteId, adminId, editorId) = await Build002SnapshotAsync(connectionString).ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        var admin = await IdentitySeed.CreateSignedInClientAsync(app, adminId).ConfigureAwait(true);

        // Sites and host names survive and still resolve.
        var sites = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        var site = Assert.Single(sites.EnumerateArray());
        Assert.Equal("Upgraded", site.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClientForHost("upgraded.test").GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        // Accounts and grants survive: the Editor still sees exactly their site.
        var editor = await IdentitySeed.CreateSignedInClientAsync(app, editorId).ConfigureAwait(true);
        var editorSites = await editor.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(siteId, Assert.Single(editorSites.EnumerateArray()).GetProperty("id").GetGuid());

        // The new schema exists, every library is empty, and uploads work.
        var library = await MediaApi.ListOkAsync(admin, siteId).ConfigureAwait(true);
        Assert.Empty(library.Items);
        Assert.Equal(0, library.TotalCount);

        var uploaded = await MediaApi.UploadOkAsync(editor, siteId).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await MediaApi.PublicAsync(app, "upgraded.test", uploaded.PublicPath).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>The 002 schema with one site, an Administrator and an Editor on that site.</summary>
    private static async Task<(Guid SiteId, Guid AdminId, Guid EditorId)> Build002SnapshotAsync(string connectionString)
    {
        Guid siteId;
        await using (var sites = new SitesDbContext(
            new DbContextOptionsBuilder<SitesDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", SitesDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await sites.Database.MigrateAsync().ConfigureAwait(false);
            var site = Site.Create("Upgraded", ["upgraded.test"], TimeProvider.System).Value;
            sites.Sites.Add(site);
            await sites.SaveChangesAsync().ConfigureAwait(false);
            siteId = site.Id;
        }

        await using var identity = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
                .Options);
        await identity.Database.MigrateAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var installation = Installation.CreateUnclaimed("upgrade-test-code", now);
        installation.Claim(now);
        identity.Installation.Add(installation);

        var admin = User.Create("Upgrade Admin", "upgrade-admin@example.test", now).Value;
        var editor = User.Create("Upgrade Editor", "upgrade-editor@example.test", now).Value;
        identity.Users.AddRange(admin, editor);
        identity.RoleGrants.Add(RoleGrant.Administrator(admin.Id, grantedByUserId: null, now));
        identity.RoleGrants.Add(RoleGrant.ForSite(editor.Id, siteId, SystemRole.Editor, grantedByUserId: null, now));
        await identity.SaveChangesAsync().ConfigureAwait(false);

        return (siteId, admin.Id, editor.Id);
    }

    private sealed class NoSite : ISiteContext
    {
        public static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
