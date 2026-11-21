using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Preview;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using Xunit;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// Preview links never travel in an archive, and an import leaves the site with none (022 spec
/// edge case "Export and import", research PV-20).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinksImportTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_restore_import_ends_every_preview_link_of_the_site_even_to_pages_that_come_back()
    {
        using var site = await FullSite.CreateAsync(database, "preview-import").ConfigureAwait(true);
        var link = await PreviewLinksApi.CreateOkAsync(site.Admin, site.Site, "page", site.About, DateTimeOffset.UtcNow.AddDays(1)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await PreviewLinksApi.OpenAsync(site.App, link.Path, "cms-a.test").ConfigureAwait(true)).Status);

        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        Assert.DoesNotContain("preview", System.Text.Encoding.UTF8.GetString(archive), StringComparison.OrdinalIgnoreCase);

        var id = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        var check = await TransferApi.CheckAsync(site.App, site.Admin, id, "restore", site.Site).ConfigureAwait(true);
        await PagesApi.OkAsync(
                await TransferApi.ConfirmAsync(site.Admin, id, new { confirmSiteName = "Site A", acceptWarnings = true, checkFingerprint = check.GetProperty("checkFingerprint").GetString() })
                    .ConfigureAwait(true),
                HttpStatusCode.Accepted)
            .ConfigureAwait(true);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);
        Assert.Equal("succeeded", (await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true)).GetProperty("state").GetString());

        // The page is back under the same id; its old link is not.
        Assert.Equal(site.About, (await PagesApi.NodeAsync(site.Admin, site.Site, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewLinksApi.OpenAsync(site.App, link.Path, "cms-a.test").ConfigureAwait(true)).Status);
        var listed = await (await PreviewLinksApi.ListAsync(site.Admin, site.Site, "page", site.About).ConfigureAwait(true)).Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Equal("[]", listed);
    }
}
