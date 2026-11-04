using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// Uploading, checking and confirming an import (020 FR-011, FR-016, FR-020, FR-035; research
/// ST-17; contracts/transfer-api.yaml).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ImportEndpointsTests(DatabaseFixture database)
{
    [Fact]
    public async Task An_upload_over_the_configured_size_is_refused_and_nothing_is_kept()
    {
        using var app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("import-too-large").ConfigureAwait(true),
            settings: new Dictionary<string, string> { ["SiteTransfer:MaxUploadSizeBytes"] = "1048576" });
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);

        var reserved = await PagesApi.OkAsync(await admin.PostAsync(new Uri("/api/v1/transfers/uploads", UriKind.Relative), content: null).ConfigureAwait(true), HttpStatusCode.Created).ConfigureAwait(true);
        using var body = new ByteArrayContent(new byte[(1024 * 1024) + 1]);
        var put = await admin.PutAsync(new Uri($"/api/v1/transfers/uploads/{TransferApi.Id(reserved)}", UriKind.Relative), body).ConfigureAwait(true);

        await PagesApi.RefusedAsync(put, HttpStatusCode.RequestEntityTooLarge, "transfer.too_large").ConfigureAwait(true);
        Assert.Equal("uploading", (await TransferApi.GetOkAsync(admin, TransferApi.Id(reserved)).ConfigureAwait(true)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_new_site_whose_archive_host_names_are_all_taken_needs_one_and_warnings_must_be_accepted()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "import-host-names").ConfigureAwait(true);
        var (_, archive) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        var id = await TransferApi.UploadAsync(scenario.Admin, archive).ConfigureAwait(true);
        var checkedTransfer = await TransferApi.CheckAsync(scenario.App, scenario.Admin, id, "new_site").ConfigureAwait(true);

        var result = checkedTransfer.GetProperty("checkResult");
        Assert.Equal([PagesScenario.HostA], result.GetProperty("hostNamesLeftOut").EnumerateArray().Select(h => h.GetString()));
        var fingerprint = checkedTransfer.GetProperty("checkFingerprint").GetString();

        await PagesApi.RefusedAsync(
            await TransferApi.ConfirmAsync(scenario.Admin, id, new { newSiteName = "Copy", checkFingerprint = fingerprint }).ConfigureAwait(true),
            HttpStatusCode.UnprocessableEntity, "transfer.warnings_not_accepted").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await TransferApi.ConfirmAsync(scenario.Admin, id, new { newSiteName = "Copy", acceptWarnings = true, checkFingerprint = fingerprint }).ConfigureAwait(true),
            HttpStatusCode.UnprocessableEntity, "transfer.invalid_options").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await TransferApi.ConfirmAsync(scenario.Admin, id, new { newSiteName = "Copy", newSiteHostName = PagesScenario.HostB, acceptWarnings = true }).ConfigureAwait(true),
            HttpStatusCode.Conflict, "sites.host_name_taken").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await TransferApi.ConfirmAsync(scenario.Admin, id, new { newSiteName = "Copy", newSiteHostName = "copy.test", acceptWarnings = true, conflictPolicy = "skip" }).ConfigureAwait(true),
            HttpStatusCode.UnprocessableEntity, "transfer.invalid_options").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Accepted, (await TransferApi.ConfirmAsync(scenario.Admin, id, new { newSiteName = "Copy", newSiteHostName = "copy.test", acceptWarnings = true, checkFingerprint = fingerprint }).ConfigureAwait(true)).StatusCode);
        await TransferApi.RunWorkerAsync(scenario.App).ConfigureAwait(true);
        Assert.Equal("succeeded", (await TransferApi.GetOkAsync(scenario.Admin, id).ConfigureAwait(true)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Only_administrators_upload_check_or_confirm_and_an_unknown_target_is_not_found()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "import-roles").ConfigureAwait(true);
        var designer = await scenario.UserAsync((scenario.SiteA, SystemRole.Designer)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Forbidden, (await designer.PostAsync(new Uri("/api/v1/transfers/uploads", UriKind.Relative), content: null).ConfigureAwait(true)).StatusCode);

        var (_, archive) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        var id = await TransferApi.UploadAsync(scenario.Admin, archive).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, (await TransferApi.PostJsonAsync(designer, $"/api/v1/transfers/{id}/check", new { mode = "new_site" }).ConfigureAwait(true)).StatusCode);
        await PagesApi.RefusedAsync(
            await TransferApi.PostJsonAsync(scenario.Admin, $"/api/v1/transfers/{id}/check", new { mode = "merge", targetSiteId = Guid.NewGuid() }).ConfigureAwait(true),
            HttpStatusCode.NotFound, "sites.not_found").ConfigureAwait(true);
    }
}
