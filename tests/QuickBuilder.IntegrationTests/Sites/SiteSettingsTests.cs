using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 User Story 1: an administrator creates and edits a site's settings (FR-001 to
/// FR-006; research S-05 to S-09).
/// </summary>
/// <remarks>
/// Also the dedicated test for the cross-site read in <c>UpdateSettings</c>: the admin
/// surface has no resolved site, so loading the named site must bypass the filter.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SiteSettingsTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("site-settings").ConfigureAwait(false));
        _admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US1 scenario 1: a 001-shaped body gets every default.</summary>
    [Fact]
    public async Task A_site_created_without_settings_is_enabled_https_english_with_its_first_host_name_primary()
    {
        var site = await SitesApi.CreateOkAsync(_admin, "Plain", ["plain.test", "www.plain.test"]).ConfigureAwait(true);

        Assert.Equal("enabled", site.GetProperty("status").GetString());
        Assert.Equal("https", site.GetProperty("scheme").GetString());
        Assert.Equal("en", site.GetProperty("defaultLanguage").GetString());
        Assert.Equal("plain.test", site.GetProperty("primaryHostName").GetString());
        Assert.Equal("https://plain.test/", site.GetProperty("baseUrl").GetString());
        Assert.True(site.GetProperty("hostNameDetails")[0].GetProperty("isPrimary").GetBoolean());
    }

    [Fact]
    public async Task A_site_can_be_created_with_every_setting_in_any_equivalent_spelling()
    {
        var site = await SitesApi.CreateOkAsync(
            _admin, "Client", ["client.test", "bücher.test"], primaryHostName: "BÜCHER.test.", scheme: "HTTP", defaultLanguage: "pt-br").ConfigureAwait(true);

        Assert.Equal("xn--bcher-kva.test", site.GetProperty("primaryHostName").GetString());
        Assert.Equal("http", site.GetProperty("scheme").GetString());
        Assert.Equal("pt-BR", site.GetProperty("defaultLanguage").GetString());
        Assert.Equal("http://xn--bcher-kva.test/", site.GetProperty("baseUrl").GetString());
        Assert.Equal("xn--bcher-kva.test", site.GetProperty("hostNames")[0].GetString());

        var details = site.GetProperty("hostNameDetails")[0];
        Assert.Equal("bücher.test", details.GetProperty("displayName").GetString());
    }

    /// <summary>
    /// US1 scenario 3, FR-006, 001 FR-008: every invalid input is reported at once, each
    /// under its own field.
    /// </summary>
    [Fact]
    public async Task Invalid_creation_settings_are_all_reported_together()
    {
        var response = await SitesApi.CreateAsync(
            _admin, "", ["ok.test"], primaryHostName: "elsewhere.test", scheme: "ftp", defaultLanguage: "klingon").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await SitesApi.BodyAsync(response).ConfigureAwait(true);
        Assert.Equal("request.invalid", problem.GetProperty("errorCode").GetString());

        var errors = problem.GetProperty("errors");
        foreach (var field in new[] { "Name", "PrimaryHostName", "Scheme", "DefaultLanguage" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"'{field}' was not reported");
        }
    }

    /// <summary>US1 scenario 2: every editable setting changes, and the base URL follows.</summary>
    [Fact]
    public async Task Settings_change_together_and_the_base_url_follows_the_primary_and_scheme()
    {
        var created = await SitesApi.CreateOkAsync(_admin, "Before", ["before.test", "www.before.test"]).ConfigureAwait(true);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetUInt32();

        var response = await SitesApi.PutSettingsAsync(_admin, id, "  After  ", "http", "DE", "WWW.before.test", version).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await SitesApi.BodyAsync(response).ConfigureAwait(true);
        Assert.Equal("After", saved.GetProperty("name").GetString());
        Assert.Equal("http", saved.GetProperty("scheme").GetString());
        Assert.Equal("de", saved.GetProperty("defaultLanguage").GetString());
        Assert.Equal("www.before.test", saved.GetProperty("primaryHostName").GetString());
        Assert.Equal("http://www.before.test/", saved.GetProperty("baseUrl").GetString());
        Assert.Equal("www.before.test", saved.GetProperty("hostNames")[0].GetString());
        Assert.NotEqual(version, saved.GetProperty("version").GetUInt32());

        // Read back through the list too: every surface shows the same site.
        var listed = (await SitesApi.BodyAsync(await SitesApi.ListAsync(_admin).ConfigureAwait(true)).ConfigureAwait(true))
            .EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);
        Assert.Equal("After", listed.GetProperty("name").GetString());
        Assert.Equal("de", listed.GetProperty("defaultLanguage").GetString());
    }

    [Fact]
    public async Task A_primary_that_is_not_one_of_the_sites_host_names_is_refused_and_nothing_changes()
    {
        var created = await SitesApi.CreateOkAsync(_admin, "Mine", ["mine.test"]).ConfigureAwait(true);
        var id = created.GetProperty("id").GetGuid();
        await SitesApi.CreateOkAsync(_admin, "Theirs", ["theirs.test"]).ConfigureAwait(true);

        var response = await SitesApi.PutSettingsAsync(
            _admin, id, "Renamed", "https", "en", "theirs.test", created.GetProperty("version").GetUInt32()).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("sites.primary_host_name_invalid", await SitesApi.ErrorCodeAsync(response).ConfigureAwait(true));
        Assert.Equal("Mine", (await SitesApi.GetOkAsync(_admin, id).ConfigureAwait(true)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Invalid_settings_are_reported_together()
    {
        var created = await SitesApi.CreateOkAsync(_admin, "Valid", ["valid.test"]).ConfigureAwait(true);

        var response = await SitesApi.PutSettingsAsync(
            _admin, created.GetProperty("id").GetGuid(), " ", "gopher", "xx", "valid.test", created.GetProperty("version").GetUInt32()).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await SitesApi.BodyAsync(response).ConfigureAwait(true)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Scheme", out _));
        Assert.True(errors.TryGetProperty("DefaultLanguage", out _));
    }

    /// <summary>US1 scenario 5, FR-005: an edit based on stale settings never overwrites.</summary>
    [Fact]
    public async Task A_stale_version_is_refused_as_out_of_date_and_changes_nothing()
    {
        var created = await SitesApi.CreateOkAsync(_admin, "Shared", ["shared.test"]).ConfigureAwait(true);
        var id = created.GetProperty("id").GetGuid();
        var seen = created.GetProperty("version").GetUInt32();

        Assert.Equal(HttpStatusCode.OK, (await SitesApi.PutSettingsAsync(_admin, id, "First save", "https", "en", "shared.test", seen).ConfigureAwait(true)).StatusCode);

        var stale = await SitesApi.PutSettingsAsync(_admin, id, "Second save", "https", "fr", "shared.test", seen).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("sites.out_of_date", await SitesApi.ErrorCodeAsync(stale).ConfigureAwait(true));
        var current = await SitesApi.GetOkAsync(_admin, id).ConfigureAwait(true);
        Assert.Equal("First save", current.GetProperty("name").GetString());
        Assert.Equal("en", current.GetProperty("defaultLanguage").GetString());
    }

    /// <summary>
    /// Research S-05: making a host name primary and removing it at the same time can never
    /// leave the site without a primary. Whichever commits first wins; the other sees it.
    /// </summary>
    [Fact]
    public async Task Racing_a_primary_change_against_removal_of_that_name_always_leaves_a_valid_primary()
    {
        for (var round = 0; round < 10; round++)
        {
            var created = await SitesApi.CreateOkAsync(_admin, $"Race {round}", [$"race{round}.test", $"www.race{round}.test"]).ConfigureAwait(true);
            var id = created.GetProperty("id").GetGuid();
            var version = created.GetProperty("version").GetUInt32();

            var promote = SitesApi.PutSettingsAsync(_admin, id, "Race", "https", "en", $"www.race{round}.test", version);
            var remove = SitesApi.RemoveHostNameAsync(_admin, id, $"www.race{round}.test");
            await Task.WhenAll(promote, remove).ConfigureAwait(true);

            var site = await SitesApi.GetOkAsync(_admin, id).ConfigureAwait(true);
            var primary = site.GetProperty("primaryHostName").GetString();
            var hostNames = site.GetProperty("hostNames").EnumerateArray().Select(h => h.GetString()).ToList();

            Assert.Contains(primary, hostNames);
            Assert.True(
                (await promote.ConfigureAwait(true)).IsSuccessStatusCode ^ (await remove.ConfigureAwait(true)).IsSuccessStatusCode,
                "exactly one of the two racing changes must win");
        }
    }
}
