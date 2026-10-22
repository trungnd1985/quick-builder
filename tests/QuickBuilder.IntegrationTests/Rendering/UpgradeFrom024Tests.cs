using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Upgrading from 024 to 018 changes nothing visitors see (018 FR-041; constitution Principle
/// III): the Forms migration adds the <c>forms</c> schema and nothing else, every existing page
/// renders the same body under the same Content-Security-Policy, and no stored document changes.
/// </summary>
/// <remarks>
/// The 024 snapshot is built rather than stored, as in 017: content is written through the API,
/// every page is rendered, then the <c>forms</c> schema is dropped, which is exactly what 024
/// did not have; 018 changes no other module's schema. The application then starts against it,
/// as replacing the image would, and runs <c>InitialForms</c>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class UpgradeFrom024Tests(DatabaseFixture database)
{
    private const string Host = "upgrade024.test";

    private sealed record Visit(string Body, string Csp);

    [Fact]
    public async Task Pages_render_the_same_body_and_policy_and_only_the_forms_schema_is_added()
    {
        var connectionString = await database.CreateDatabaseAsync("upgrade-024").ConfigureAwait(true);
        string[] paths = ["/", "/about", "/contact"];
        var before = new Dictionary<string, Visit>();

        using (var app = new QuickBuilderApp(connectionString))
        {
            using var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);
            var site = await Media.MediaApi.CreateSiteAsync(admin, "Upgrade 024", Host).ConfigureAwait(true);

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
        var tablesBefore = await TablesOutsideFormsAsync(connectionString).ConfigureAwait(true);
        await ExecuteAsync(connectionString, "DROP SCHEMA forms CASCADE").ConfigureAwait(true);

        using (var upgraded = new QuickBuilderApp(connectionString))
        {
            _ = upgraded.Services;
            Assert.Equal(1, await CountAsync(connectionString, "SELECT count(*) FROM forms.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_InitialForms'").ConfigureAwait(true));
            Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM forms.forms").ConfigureAwait(true));
            Assert.Equal(0, await CountAsync(connectionString, "SELECT count(*) FROM forms.submissions").ConfigureAwait(true));

            using var visitor = upgraded.CreateClientForHost(Host);
            foreach (var path in paths)
            {
                var after = await VisitAsync(visitor, path).ConfigureAwait(true);
                Assert.Equal(before[path].Body, after.Body);
                Assert.Equal(before[path].Csp, after.Csp);
                Assert.Contains("form-action 'none'", after.Csp, StringComparison.Ordinal);
                Assert.DoesNotContain("connect-src", after.Csp, StringComparison.Ordinal);
            }
        }

        Assert.Equal(documentsBefore, await DocumentsAsync(connectionString).ConfigureAwait(true));
        Assert.Equal(tablesBefore, await TablesOutsideFormsAsync(connectionString).ConfigureAwait(true));
    }

    private static async Task<Visit> VisitAsync(HttpClient visitor, string path)
    {
        using var response = await visitor.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new Visit(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false),
            response.Headers.TryGetValues("Content-Security-Policy", out var csp) ? string.Join(';', csp) : string.Empty);
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

    /// <summary>Every table and column outside <c>forms</c>: the upgrade must not touch them.</summary>
    private static async Task<List<string>> TablesOutsideFormsAsync(string connectionString)
    {
        var result = new List<string>();
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT table_schema || '.' || table_name || '.' || column_name || ':' || data_type FROM information_schema.columns " +
            "WHERE table_schema NOT IN ('forms', 'pg_catalog', 'information_schema') ORDER BY 1",
            connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }
}
