using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>Archives leave the trash out, and a restore import empties it (021 FR-030, research TR-17).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransferTrashTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_export_leaves_trashed_pages_out()
    {
        using var site = await FullSite.CreateAsync(database, "trash-export").ConfigureAwait(true);
        var doomed = (await PagesApi.CreateOkAsync(site.Admin, site.Site, "doomed").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(site.Admin, site.Site, doomed, 1).ConfigureAwait(true)).ConfigureAwait(true);

        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        await using var opened = await TransferApi.OpenAsync(archive).ConfigureAwait(true);
        var pages = await TransferApi.LinesAsync(opened, "pages", "tree").ConfigureAwait(true);
        Assert.NotEmpty(pages);
        Assert.DoesNotContain(pages, p => p.GetProperty("id").GetGuid() == doomed);
    }

    [Fact]
    public async Task A_restore_import_brings_back_what_was_trashed_since_and_empties_the_trash()
    {
        using var site = await FullSite.CreateAsync(database, "trash-restore-import").ConfigureAwait(true);
        var keep = (await PagesApi.CreateOkAsync(site.Admin, site.Site, "keep-me").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        // In the archive and now in the trash: the same id on both sides must not collide.
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(site.Admin, site.Site, keep, 1).ConfigureAwait(true)).ConfigureAwait(true);

        var id = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        var check = await TransferApi.CheckAsync(site.App, site.Admin, id, "restore", site.Site).ConfigureAwait(true);
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await TransferApi.ConfirmAsync(site.Admin, id, new { confirmSiteName = "Site A", acceptWarnings = true, checkFingerprint = check.GetProperty("checkFingerprint").GetString() }).ConfigureAwait(true)).StatusCode);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);

        var done = await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true);
        Assert.Equal("succeeded", done.GetProperty("state").GetString());
        Assert.Equal(keep, (await PagesApi.NodeAsync(site.Admin, site.Site, "/keep-me").ConfigureAwait(true)).GetProperty("id").GetGuid());

        var trash = await PagesApi.OkAsync(await site.Admin.GetAsync(new Uri($"/api/v1/pages/sites/{site.Site}/trash/pages", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(0, trash.GetProperty("total").GetInt32());
    }
}
