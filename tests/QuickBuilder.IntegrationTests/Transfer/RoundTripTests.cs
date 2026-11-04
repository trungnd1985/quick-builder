using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// A site exported and imported as a new site is the same site, apart from its ids and the host
/// names left out (020 SC-001, FR-017 … FR-021, FR-037).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RoundTripTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_export_imported_as_a_new_site_holds_the_same_content_under_new_ids()
    {
        using var site = await FullSite.CreateAsync(database, "round-trip").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        var done = await TransferApi.ImportAsNewSiteAsync(site.App, site.Admin, archive, "Acme copy", hostName: "copy.test").ConfigureAwait(true);
        var copy = done.GetProperty("siteId").GetGuid();
        Assert.NotEqual(site.Site, copy);

        // The site: its name, the host name given (the archive's are taken here), its search settings.
        var created = await PagesApi.OkAsync(await site.Admin.GetAsync(new Uri($"/api/v1/sites/{copy}", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("Acme copy", created.GetProperty("name").GetString());
        Assert.Equal(["copy.test"], created.GetProperty("hostNames").EnumerateArray().Select(h => h.GetString()));

        // Pages: the same tree and addresses, under new ids.
        Assert.Equal(
            (await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true)).Order(),
            (await PagesApi.PathsAsync(site.Admin, copy).ConfigureAwait(true)).Order());
        var about = (await PagesApi.NodeAsync(site.Admin, copy, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid();
        Assert.NotEqual(site.About, about);

        // The draft is ahead of the two published versions, as on the source.
        var draft = await PagesApi.DraftOkAsync(site.Admin, PagesApi.PagePath(copy, about)).ConfigureAwait(true);
        Assert.Contains("About Acme (draft)", draft.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(site.Header.ToString(), draft.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(site.Admin, PagesApi.PagePath(copy, about)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal([1, 2], history.GetProperty("retainedVersions").EnumerateArray().Select(v => v.GetInt32()).Order());

        // Visitors see the live version at the new site's host name.
        var visitor = site.App.CreateClientForHost("copy.test");
        var page = await visitor.GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("About Acme", await page.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);

        // Items: the post's reference to the person points at the imported person.
        var collections = (await PagesApi.OkAsync(await QuickBuilder.IntegrationTests.Cms.CmsApi.ListAsync(site.Admin, copy).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("collections");
        Guid Collection(string slug) => collections.EnumerateArray().Single(c => c.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();
        var people = await ItemsApi.ListOkAsync(site.Admin, copy, Collection("people")).ConfigureAwait(true);
        var person = people.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        var posts = await ItemsApi.ListOkAsync(site.Admin, copy, Collection("posts")).ConfigureAwait(true);
        var post = await ItemsApi.GetOkAsync(site.Admin, copy, Collection("posts"), posts.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid()).ConfigureAwait(true);
        Assert.NotEqual(site.Person, person);
        Assert.Equal(person.ToString(), ItemsApi.Values(post)["f9"]!.GetValue<string>());
        Assert.Equal(person.ToString(), ItemsApi.Values(post, "published")["f9"]!.GetValue<string>());
        Assert.Contains("Hello, again", ItemsApi.Values(post).ToJsonString(), StringComparison.Ordinal);

        // The menu and the redirect point at the imported about page.
        var menus = await PagesApi.OkAsync(await MenusApi.ListAsync(site.Admin, copy).ConfigureAwait(true)).ConfigureAwait(true);
        var menu = menus.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Contains(about.ToString(), (await MenusApi.GetOkAsync(site.Admin, copy, menu).ConfigureAwait(true)).GetProperty("draft").GetRawText(), StringComparison.OrdinalIgnoreCase);
        var redirects = await PagesApi.OkAsync(await QuickBuilder.IntegrationTests.Seo.RedirectsApi.ListAsync(site.Admin, copy).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains(about.ToString(), redirects.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.MovedPermanently, (await visitor.GetAsync(new Uri("/old-about", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        var report = done.GetProperty("report");
        Assert.Equal(2, report.GetProperty("imported").GetProperty("item").GetInt64());
        Assert.Equal(0, report.GetProperty("broken").GetArrayLength());
    }

    [Fact]
    public async Task Imported_into_another_installation_the_archive_keeps_its_host_names_and_names_its_authors_as_historical()
    {
        using var site = await FullSite.CreateAsync(database, "round-trip-other").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        using var other = new QuickBuilderApp(await database.CreateDatabaseAsync("round-trip-target").ConfigureAwait(true));
        var admin = await other.CreateAdminClientAsync().ConfigureAwait(true);
        var done = await TransferApi.ImportAsNewSiteAsync(other, admin, archive, "Acme").ConfigureAwait(true);
        var copy = done.GetProperty("siteId").GetGuid();

        // FR-020: the archive's host names are free on this installation, so the site takes them.
        var created = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/sites/{copy}", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("cms-a.test", created.GetProperty("hostNames").EnumerateArray().Select(h => h.GetString()));

        // FR-021: the source's members are names here, not accounts.
        Assert.True(done.GetProperty("report").GetProperty("historicalAuthors").GetInt32() > 0);
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(other.Services);
        var directory = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<QuickBuilder.Identity.Contracts.IUserDirectory>(scope.ServiceProvider);
        var identity = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<QuickBuilder.Identity.Infrastructure.IdentityDbContext>(scope.ServiceProvider);
        var historical = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(identity.HistoricalAuthors).Select(h => h.Id).ToList();
        Assert.NotEmpty(historical);
        Assert.All(await directory.FindManyAsync(historical).ConfigureAwait(true), u => Assert.True(u.IsHistorical));
        var users = await PagesApi.OkAsync(await admin.GetAsync(new Uri("/api/v1/identity/users", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.DoesNotContain(historical, id => users.GetRawText().Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));

        // FR-031: the import is a security event, with ids and no labels.
        var events = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(identity.SecurityEvents).ToList();
        Assert.DoesNotContain(events, e => e.EventType == "site_imported");
        await TransferApi.DispatchOutboxAsync(other).ConfigureAwait(true);
        var imported = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(identity.SecurityEvents).Where(e => e.EventType == "site_imported").ToList();
        Assert.Single(imported);
        Assert.DoesNotContain("Acme", imported[0].Details, StringComparison.Ordinal);
    }

    /// <summary>
    /// T040, FR-019: two imports of one archive are two sites that share no id with the source
    /// or with each other, so no row of one can reach another's (Principle V).
    /// </summary>
    [Fact]
    public async Task The_same_archive_imported_twice_gives_two_sites_sharing_no_id()
    {
        using var site = await FullSite.CreateAsync(database, "import-twice").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        var first = (await TransferApi.ImportAsNewSiteAsync(site.App, site.Admin, archive, "One", hostName: "one.test").ConfigureAwait(true)).GetProperty("siteId").GetGuid();
        var second = (await TransferApi.ImportAsNewSiteAsync(site.App, site.Admin, archive, "Two", hostName: "two.test").ConfigureAwait(true)).GetProperty("siteId").GetGuid();

        var ids = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var siteId in new[] { site.Site, first, second })
        {
            ids[siteId] = await IdsOfAsync(site.App, siteId).ConfigureAwait(true);
        }

        Assert.Equal(ids[site.Site].Count, ids[first].Count);
        Assert.Empty(ids[site.Site].Intersect(ids[first]));
        Assert.Empty(ids[site.Site].Intersect(ids[second]));
        Assert.Empty(ids[first].Intersect(ids[second]));

        // Editing one copy leaves the other unchanged.
        var oneAbout = (await PagesApi.NodeAsync(site.Admin, first, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PublishingApi.SaveAsync(site.Admin, PagesApi.PagePath(first, oneAbout), PublishingApi.HeadingDocument("Only in one")).ConfigureAwait(true);
        var twoAbout = (await PagesApi.NodeAsync(site.Admin, second, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid();
        Assert.DoesNotContain("Only in one", (await PagesApi.DraftOkAsync(site.Admin, PagesApi.PagePath(second, twoAbout)).ConfigureAwait(true)).GetRawText(), StringComparison.Ordinal);
    }

    private static async Task<HashSet<Guid>> IdsOfAsync(QuickBuilderApp app, Guid siteId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        var ids = new HashSet<Guid>();
        foreach (var table in new[] { "pages.pages", "pages.components", "pages.menus", "pages.redirects", "cms.collections", "cms.items", "media.media_files" })
        {
            await using var command = new Npgsql.NpgsqlCommand($"SELECT id FROM {table} WHERE site_id = @site", connection);
            command.Parameters.AddWithValue("site", siteId);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        return ids;
    }

    [Fact]
    public async Task Starting_fresh_imports_drafts_only_with_nothing_published_and_no_history()
    {
        using var site = await FullSite.CreateAsync(database, "start-fresh").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        var done = await TransferApi.ImportAsNewSiteAsync(site.App, site.Admin, archive, "Starter", hostName: "starter.test", startFresh: true).ConfigureAwait(true);
        var copy = done.GetProperty("siteId").GetGuid();

        var about = (await PagesApi.NodeAsync(site.Admin, copy, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(site.Admin, PagesApi.PagePath(copy, about)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(history.GetProperty("retainedVersions").EnumerateArray());
        var visitor = site.App.CreateClientForHost("starter.test");
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        var collections = await PagesApi.OkAsync(await site.Admin.GetAsync(new Uri($"/api/v1/cms/sites/{copy}/collections", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        var posts = JsonSerializer.Deserialize<JsonElement>(collections.GetRawText());
        Assert.Equal(2, (posts.ValueKind == JsonValueKind.Array ? posts : posts.GetProperty("collections")).GetArrayLength());
    }
}
