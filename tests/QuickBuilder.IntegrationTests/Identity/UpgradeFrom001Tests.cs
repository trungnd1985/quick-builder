using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Upgrading an installation that ran feature 001 (spec FR-006, SC-008; constitution
/// Principle III: "upgrade a database snapshot from the previous release").
/// </summary>
/// <remarks>
/// The snapshot is built rather than stored: only the Sites migrations are applied — the
/// exact schema 001 shipped — and sites are written into it. Then the current application
/// starts against that database, as replacing the image would.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom001Tests(DatabaseFixture database)
{
    [Fact]
    public async Task An_upgraded_installation_enters_setup_mode_and_keeps_every_site()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade").ConfigureAwait(true);
        await MigrateSitesOnlyAsync(connectionString).ConfigureAwait(true);
        await SeedSiteAsync(connectionString, "Alpha", "alpha.test").ConfigureAwait(true);
        await SeedSiteAsync(connectionString, "Beta", "beta.test").ConfigureAwait(true);

        using var app = new QuickBuilderApp(connectionString);
        var anonymous = app.CreateAnonymousClient();

        // Setup mode.
        var status = await anonymous.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.True(status.GetProperty("setupRequired").GetBoolean());

        // Public sites keep being served while setup is pending.
        foreach (var host in new[] { "alpha.test", "beta.test" })
        {
            var resolved = await app.CreateClientForHost(host).GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
            Assert.True(await PublicSite.IsResolvedAsync(resolved).ConfigureAwait(true), $"{host} no longer resolves");
        }

        // Complete setup through the real endpoint, then every site is still there.
        var code = await SetupHelpers.GetSetupCodeAsync(app).ConfigureAwait(true);
        var setup = await SetupHelpers.PostSetupAsync(anonymous, code).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        var admin = anonymous;
        admin.DefaultRequestHeaders.Add(
            "Cookie",
            $"qb_session={SetupHelpers.CookieValue(setup, "qb_session")}");

        var sites = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        var names = sites.EnumerateArray().Select(s => s.GetProperty("name").GetString()).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["Alpha", "Beta"], names);
    }

    /// <summary>Applies only the Sites module's migrations: the 001 schema.</summary>
    internal static async Task MigrateSitesOnlyAsync(string connectionString)
    {
        await using var context = CreateSitesContext(connectionString);
        await context.Database.MigrateAsync().ConfigureAwait(false);
    }

    internal static async Task SeedSiteAsync(string connectionString, string name, string hostName)
    {
        await using var context = CreateSitesContext(connectionString);
        context.Sites.Add(Site.Create(name, [hostName], TimeProvider.System).Value);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private static SitesDbContext CreateSitesContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SitesDbContext.Schema))
            .Options;

        return new SitesDbContext(options, NoSite.Instance);
    }

    private sealed class NoSite : ISiteContext
    {
        public static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
