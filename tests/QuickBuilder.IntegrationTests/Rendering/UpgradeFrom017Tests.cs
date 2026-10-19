using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Upgrading from 017 to 024 changes nothing visitors see (024 tasks T074; constitution
/// Principle III): <c>AddMenus</c> applies, every existing page renders the same body under the
/// same Content-Security-Policy, and no stored document changes.
/// </summary>
/// <remarks>
/// The 017 snapshot is built rather than stored, as in 016: content is written through the API,
/// every page is rendered, then Pages is migrated down to its 017 schema (<c>AddSeo</c>). The
/// application then starts against it, as replacing the image would, and runs 024's migration.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom017Tests(DatabaseFixture database)
{
    private const string PagesAt017 = "20261013122918_AddSeo";
    private const string Host = "upgrade017.test";

    private sealed record Visit(string Body, string Csp);

    [Fact]
    public async Task Pages_render_the_same_body_and_policy_and_no_document_changes()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-017").ConfigureAwait(true);
        string[] paths = ["/", "/about", "/contact"];
        var before = new Dictionary<string, Visit>();

        using (var app = new QuickBuilderApp(connectionString))
        {
            using var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
            var site = await Media.MediaApi.CreateSiteAsync(admin, "Upgrade 017", Host).ConfigureAwait(true);

            var header = (await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
            var headerPath = PagesApi.ComponentPath(site, header);
            await PublishingApi.SaveAsync(admin, headerPath, PublishingApi.HeadingDocument("Header")).ConfigureAwait(true);
            await PublishingApi.PublishCurrentAsync(admin, headerPath).ConfigureAwait(true);

            var pages = await PagesApi.SeedTreeAsync(admin, site, "home*\nabout\ncontact").ConfigureAwait(true);
            foreach (var (path, id) in pages)
            {
                var subject = PagesApi.PagePath(site, id);
                await PublishingApi.SaveAsync(admin, subject, PublishingApi.HeadingDocument($"Heading of {path}", header)).ConfigureAwait(true);
                await PublishingApi.PublishCurrentAsync(admin, subject).ConfigureAwait(true);
            }

            using var visitor = app.CreateClientForHost(Host);
            foreach (var path in paths)
            {
                before[path] = await VisitAsync(visitor, path).ConfigureAwait(true);
            }
        }

        var documentsBefore = await DocumentsAsync(connectionString).ConfigureAwait(true);
        await DowngradeTo017Async(connectionString).ConfigureAwait(true);

        using (var upgraded = new QuickBuilderApp(connectionString))
        {
            _ = upgraded.Services;
            Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM pages.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddMenus'").ConfigureAwait(true));
            Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM pages.menus").ConfigureAwait(true));

            using var visitor = upgraded.CreateClientForHost(Host);
            foreach (var path in paths)
            {
                var after = await VisitAsync(visitor, path).ConfigureAwait(true);
                Assert.Equal(before[path].Body, after.Body);
                Assert.Equal(before[path].Csp, after.Csp);
                Assert.DoesNotContain("script-src", after.Csp, StringComparison.Ordinal);
            }
        }

        Assert.Equal(documentsBefore, await DocumentsAsync(connectionString).ConfigureAwait(true));
    }

    private static async Task<Visit> VisitAsync(HttpClient visitor, string path)
    {
        using var response = await visitor.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new Visit(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false),
            response.Headers.TryGetValues("Content-Security-Policy", out var csp) ? string.Join(';', csp) : string.Empty);
    }

    /// <summary>Pages back to its 017 schema; 024's tables are gone, as they were at 017.</summary>
    private static async Task DowngradeTo017Async(string connectionString)
    {
        await using (var pages = new PagesDbContext(
            new DbContextOptionsBuilder<PagesDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", PagesDbContext.Schema))
                .Options,
            NoSite.Instance))
        {
            await pages.GetService<IMigrator>().MigrateAsync(PagesAt017).ConfigureAwait(false);
        }

        Assert.Equal(0, await CountAsync(
            connectionString,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'pages' AND table_name LIKE 'menu%'").ConfigureAwait(false));
    }

    /// <summary>Every stored draft and published document, by row, as text.</summary>
    private static async Task<Dictionary<string, string>> DocumentsAsync(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        foreach (var (table, key) in new[] { ("pages.published_versions", "id"), ("pages.page_drafts", "page_id"), ("pages.component_drafts", "component_id") })
        {
            await using var command = new Npgsql.NpgsqlCommand($"SELECT {key}::text, document::text FROM {table}", connection);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result[$"{table}/{reader.GetString(0)}"] = reader.GetString(1);
            }
        }

        Assert.NotEmpty(result);
        return result;
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
