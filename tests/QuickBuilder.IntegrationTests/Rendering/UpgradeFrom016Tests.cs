using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Upgrading from 016 to 017 changes nothing visitors see in a page's body: the three migrations
/// roll back and apply again, pages render the same, the head gains the canonical address and
/// the sharing tags, existing sites keep serving every host name, and robots.txt and the sitemap
/// answer (017 tasks T108; constitution Principle III).
/// </summary>
/// <remarks>
/// As in <see cref="UpgradeFrom035Tests"/>, the 016 snapshot is built: content is written through
/// the API, then Pages, Cms and Sites are migrated down to their 016 schemas and the application
/// starts against them, as replacing the image would.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom016Tests(DatabaseFixture database)
{
    private const string PagesAt016 = "20261010162709_AddTemplates";
    private const string CmsAt016 = "20261011043703_AddPublishedText";
    private const string SitesAt016 = "20260925181330_SiteSettingsAndLifecycle";
    private const string Host = "upgrade016.test";

    [Fact]
    public async Task Pages_render_the_same_and_the_site_gains_what_017_adds()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-016").ConfigureAwait(true);
        string[] paths = ["/", "/about", "/news"];
        var before = new Dictionary<string, string>();

        using (var app = new QuickBuilderApp(connectionString))
        {
            using var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
            var site = await Media.MediaApi.CreateSiteAsync(admin, "Upgrade 016", Host).ConfigureAwait(true);
            var pages = await PagesApi.SeedTreeAsync(admin, site, "home*\nabout\nnews").ConfigureAwait(true);
            foreach (var (path, id) in pages)
            {
                var subject = PagesApi.PagePath(site, id);
                await PublishingApi.SaveAsync(admin, subject, PublishingApi.HeadingDocument($"Heading of {path}")).ConfigureAwait(true);
                await PublishingApi.PublishCurrentAsync(admin, subject).ConfigureAwait(true);
            }

            using var visitor = app.CreateClientForHost(Host);
            foreach (var path in paths)
            {
                before[path] = await visitor.GetStringAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
            }
        }

        await DowngradeTo016Async(connectionString).ConfigureAwait(true);

        using var upgraded = new QuickBuilderApp(connectionString);
        _ = upgraded.Services;
        foreach (var (schema, migration) in new[] { ("pages", "AddSeo"), ("cms", "AddScheduleRedirect"), ("sites", "AddSearchSettings") })
        {
            Assert.Equal(1, await CountAsync(connectionString, $"SELECT count(*) FROM {schema}.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_{migration}'").ConfigureAwait(true));
        }

        using var client = upgraded.CreateClientForHost(Host);
        foreach (var path in paths)
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

            Assert.Equal(Between(before[path], "<body", "</body>"), Between(html, "<body", "</body>"));
            Assert.Equal(Between(before[path], "<title>", "</title>"), Between(html, "<title>", "</title>"));
            Assert.Contains($"<link rel=\"canonical\" href=\"https://{Host}{path}\">", html, StringComparison.Ordinal);
            Assert.Contains("<meta property=\"og:title\"", html, StringComparison.Ordinal);
        }

        // Existing sites keep serving every host name; the setting is theirs to turn on.
        Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM sites.sites WHERE redirect_other_hosts").ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/robots.txt", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/sitemap.xml", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    private static string Between(string html, string start, string end)
    {
        var from = html.IndexOf(start, StringComparison.Ordinal);
        var to = html.IndexOf(end, from, StringComparison.Ordinal);
        return html[from..to];
    }

    /// <summary>Pages, Cms and Sites back to their 016 schemas; rolling back proves each migration's down step.</summary>
    private static async Task DowngradeTo016Async(string connectionString)
    {
        await using (var pages = new PagesDbContext(Options<PagesDbContext>(connectionString, PagesDbContext.Schema), NoSite.Instance))
        {
            await pages.GetService<IMigrator>().MigrateAsync(PagesAt016).ConfigureAwait(false);
        }

        await using (var cms = new CmsDbContext(Options<CmsDbContext>(connectionString, CmsDbContext.Schema), NoSite.Instance))
        {
            await cms.GetService<IMigrator>().MigrateAsync(CmsAt016).ConfigureAwait(false);
        }

        await using (var sites = new SitesDbContext(Options<SitesDbContext>(connectionString, SitesDbContext.Schema), NoSite.Instance))
        {
            await sites.GetService<IMigrator>().MigrateAsync(SitesAt016).ConfigureAwait(false);
        }

        // 017's tables and columns are gone, as they were at 016.
        Assert.Equal(0, await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'pages' AND table_name IN ('redirects', 'sitemap_state', 'redirect_changes')").ConfigureAwait(false));
        Assert.Equal(0, await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'sites' AND column_name = 'redirect_other_hosts'").ConfigureAwait(false));
    }

    private static DbContextOptions<T> Options<T>(string connectionString, string schema)
        where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;

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
