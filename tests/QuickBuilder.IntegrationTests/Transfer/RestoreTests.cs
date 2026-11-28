using System.Net;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// Restoring a site from its own archive makes it match the archive, after a preview and a typed
/// confirmation, and first exports the site as it was (020 FR-027a … FR-027d, SC-005).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RestoreTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_restore_previews_its_removals_makes_the_site_match_the_archive_and_can_itself_be_undone()
    {
        using var site = await FullSite.CreateAsync(database, "restore").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);
        var pathsAtExport = (await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true)).Order().ToList();

        // Changes after the backup: a new page and a new file.
        var news = (await PagesApi.CreateOkAsync(site.Admin, site.Site, "news").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var upload = await MediaApi.UploadOkAsync(site.Admin, site.Site).ConfigureAwait(true);
        var hostsBefore = await HostNamesAsync(site).ConfigureAwait(true);

        var id = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        var checkedTransfer = await TransferApi.CheckAsync(site.App, site.Admin, id, "restore", site.Site).ConfigureAwait(true);
        Assert.Equal("awaiting_confirmation", checkedTransfer.GetProperty("state").GetString());

        // FR-027b: the preview lists what goes.
        var removals = checkedTransfer.GetProperty("checkResult").GetProperty("removals").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(news, removals);
        Assert.Contains(upload.Id, removals);
        Assert.True(checkedTransfer.GetProperty("checkResult").GetProperty("archive").GetProperty("sameSiteAsTarget").GetBoolean());

        var fingerprint = checkedTransfer.GetProperty("checkFingerprint").GetString();
        await PagesApi.RefusedAsync(
            await TransferApi.ConfirmAsync(site.Admin, id, new { confirmSiteName = "Not the name", acceptWarnings = true, checkFingerprint = fingerprint }).ConfigureAwait(true),
            HttpStatusCode.UnprocessableEntity, "transfer.confirmation_mismatch").ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Accepted, (await TransferApi.ConfirmAsync(site.Admin, id, new { confirmSiteName = "Site A", acceptWarnings = true, checkFingerprint = fingerprint }).ConfigureAwait(true)).StatusCode);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);

        var done = await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true);
        Assert.Equal("succeeded", done.GetProperty("state").GetString());
        Assert.Equal(site.Site, done.GetProperty("siteId").GetGuid());

        // FR-027c: the site equals the archive again, under its original ids; host names stay.
        Assert.Equal(pathsAtExport, (await PagesApi.PathsAsync(site.Admin, site.Site).ConfigureAwait(true)).Order().ToList());
        Assert.Equal(site.About, (await PagesApi.NodeAsync(site.Admin, site.Site, "/about").ConfigureAwait(true)).GetProperty("id").GetGuid());
        var files = await MediaApi.ListOkAsync(site.Admin, site.Site).ConfigureAwait(true);
        Assert.DoesNotContain(files.Items, f => f.Id == upload.Id);
        Assert.Contains(files.Items, f => f.Id == site.World.Items.Image);
        Assert.Equal(hostsBefore, await HostNamesAsync(site).ConfigureAwait(true));
        var visitor = site.App.CreateClientForHost("cms-a.test");
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(new Uri("/news", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        // FR-027d: the site as it was before is kept as an export, and restoring it undoes the restore.
        var preRestore = done.GetProperty("report").GetProperty("preRestoreTransferId").GetGuid();
        var kept = await TransferApi.GetOkAsync(site.Admin, preRestore).ConfigureAwait(true);
        Assert.Equal(("succeeded", "pre_restore", true), (kept.GetProperty("state").GetString(), kept.GetProperty("trigger").GetString(), kept.GetProperty("downloadable").GetBoolean()));
        var before = await (await TransferApi.DownloadAsync(site.Admin, preRestore).ConfigureAwait(true)).Content.ReadAsByteArrayAsync().ConfigureAwait(true);

        var undo = await TransferApi.UploadAsync(site.Admin, before).ConfigureAwait(true);
        var undoCheck = await TransferApi.CheckAsync(site.App, site.Admin, undo, "restore", site.Site).ConfigureAwait(true);
        await PagesApi.OkAsync(await TransferApi.ConfirmAsync(site.Admin, undo, new { confirmSiteName = "Site A", acceptWarnings = true, checkFingerprint = undoCheck.GetProperty("checkFingerprint").GetString() }).ConfigureAwait(true), HttpStatusCode.Accepted).ConfigureAwait(true);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);
        Assert.Equal("succeeded", (await TransferApi.GetOkAsync(site.Admin, undo).ConfigureAwait(true)).GetProperty("state").GetString());
        Assert.Equal(news, (await PagesApi.NodeAsync(site.Admin, site.Site, "/news").ConfigureAwait(true)).GetProperty("id").GetGuid());
        Assert.Contains((await MediaApi.ListOkAsync(site.Admin, site.Site).ConfigureAwait(true)).Items, f => f.Id == upload.Id);
    }

    // 066 FR-022, US4: borders and corner radii travel in the archive unchanged. They are changed
    // after the export, and the restore brings back exactly what was exported.
    [Fact]
    public async Task Borders_and_corner_radii_come_back_unchanged_from_an_archive()
    {
        using var site = await FullSite.CreateAsync(database, "restore-borders").ConfigureAwait(true);
        var about = PagesApi.PagePath(site.Site, site.About);
        const string bordered = """
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"s","type":"section","props":{"border":{"width":"thick","style":"dashed","color":{"token":"color-brand"},"radius":{"token":"radius-card"}}},"children":[
                {"id":"t","type":"text","props":{"text":"Framed","border":{"width":"thin","padding":{"token":"space-sm"}}},"children":[]}]}]}}
            """;
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(site.Admin, about, bordered).ConfigureAwait(true)).ConfigureAwait(true);
        var radii = await File.ReadAllTextAsync(Path.Combine(
            DraftApiTests.FindRepositoryRoot(), "docs", "contracts", "design-tokens", "examples", "valid", "with-radii.json")).ConfigureAwait(true);
        var tokens = await EditorApi.GetOkAsync(site.Admin, site.Site).ConfigureAwait(true);
        await PagesApi.OkAsync(await EditorApi.SaveAsync(site.Admin, site.Site, radii, tokens.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        var (_, archive) = await TransferApi.ExportAsync(site.App, site.Admin, site.Site).ConfigureAwait(true);

        // After the export: the borders and the radii go.
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(site.Admin, about, PublishingApi.HeadingDocument("Plain")).ConfigureAwait(true)).ConfigureAwait(true);
        var changed = System.Text.Json.Nodes.JsonNode.Parse(EditorApi.DefaultDocument)!.AsObject();
        changed.Remove("radii");
        tokens = await EditorApi.GetOkAsync(site.Admin, site.Site).ConfigureAwait(true);
        await PagesApi.OkAsync(await EditorApi.SaveAsync(site.Admin, site.Site, changed.ToJsonString(), tokens.GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        var id = await TransferApi.UploadAsync(site.Admin, archive).ConfigureAwait(true);
        var check = await TransferApi.CheckAsync(site.App, site.Admin, id, "restore", site.Site).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Accepted, (await TransferApi.ConfirmAsync(site.Admin, id, new { confirmSiteName = "Site A", acceptWarnings = true, checkFingerprint = check.GetProperty("checkFingerprint").GetString() }).ConfigureAwait(true)).StatusCode);
        await TransferApi.RunWorkerAsync(site.App).ConfigureAwait(true);
        Assert.Equal("succeeded", (await TransferApi.GetOkAsync(site.Admin, id).ConfigureAwait(true)).GetProperty("state").GetString());

        using var sentDocument = System.Text.Json.JsonDocument.Parse(bordered);
        Assert.True(System.Text.Json.JsonElement.DeepEquals(sentDocument.RootElement, (await PagesApi.DraftOkAsync(site.Admin, about).ConfigureAwait(true)).GetProperty("document")));
        using var sentTokens = System.Text.Json.JsonDocument.Parse(radii);
        Assert.True(System.Text.Json.JsonElement.DeepEquals(
            sentTokens.RootElement.GetProperty("radii"),
            (await EditorApi.GetOkAsync(site.Admin, site.Site).ConfigureAwait(true)).GetProperty("document").GetProperty("radii")));
    }

    [Fact]
    public async Task An_archive_of_another_site_cannot_restore_this_one()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "restore-other").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteB).ConfigureAwait(true);

        var id = await TransferApi.UploadAsync(scenario.Admin, archive).ConfigureAwait(true);
        var transfer = await TransferApi.CheckAsync(scenario.App, scenario.Admin, id, "restore", scenario.SiteA).ConfigureAwait(true);

        Assert.Equal("refused", transfer.GetProperty("state").GetString());
        Assert.Contains(transfer.GetProperty("checkResult").GetProperty("problems").EnumerateArray(), p => p.GetProperty("code").GetString() == "transfer.restore_not_allowed");
    }

    private static async Task<List<string?>> HostNamesAsync(FullSite site) =>
        [.. (await PagesApi.OkAsync(await site.Admin.GetAsync(new Uri($"/api/v1/sites/{site.Site}", UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("hostNames").EnumerateArray().Select(h => h.GetString()).Order()];
}
