using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// The export endpoints: who may call them, one transfer per site, downloads with ranges, and
/// cancel and delete (020 FR-006 … FR-010, FR-035; contracts/transfer-api.yaml).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransferEndpointsTests(DatabaseFixture database)
{
    [Fact]
    public async Task Only_administrators_export_list_follow_or_download()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "transfer-roles").ConfigureAwait(true);
        var (id, _) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        foreach (var role in new[] { SystemRole.Designer, SystemRole.Editor })
        {
            var member = await scenario.UserAsync((scenario.SiteA, role)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, (await TransferApi.StartExportAsync(member, scenario.SiteA).ConfigureAwait(true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await TransferApi.ListAsync(member, scenario.SiteA).ConfigureAwait(true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await TransferApi.GetAsync(member, id).ConfigureAwait(true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await TransferApi.DownloadAsync(member, id).ConfigureAwait(true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await TransferApi.DeleteArchiveAsync(member, id).ConfigureAwait(true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await TransferApi.CancelAsync(member, id).ConfigureAwait(true)).StatusCode);
        }

        var anonymous = scenario.App.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await TransferApi.DownloadAsync(anonymous, id).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_second_export_of_a_site_while_one_is_queued_is_refused_and_other_sites_are_not_held()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "transfer-busy").ConfigureAwait(true);

        await TransferApi.StartExportOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        await PagesApi.RefusedAsync(await TransferApi.StartExportAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true), HttpStatusCode.Conflict, "transfer.busy")
            .ConfigureAwait(true);
        await TransferApi.StartExportOkAsync(scenario.Admin, scenario.SiteB).ConfigureAwait(true);

        // Once the first has run, the site can be exported again.
        Assert.Equal(2, await TransferApi.RunWorkerAsync(scenario.App).ConfigureAwait(true));
        await TransferApi.StartExportOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_download_resumes_with_a_range_and_both_halves_make_the_archive()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "transfer-range").ConfigureAwait(true);
        var (id, whole) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        var first = await TransferApi.DownloadAsync(scenario.Admin, id, "bytes=0-99").ConfigureAwait(true);
        var rest = await TransferApi.DownloadAsync(scenario.Admin, id, "bytes=100-").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.PartialContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.PartialContent, rest.StatusCode);
        Assert.Equal("application/zip", first.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".qbsite", first.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);
        byte[] joined = [.. await first.Content.ReadAsByteArrayAsync().ConfigureAwait(true), .. await rest.Content.ReadAsByteArrayAsync().ConfigureAwait(true)];
        Assert.Equal(whole, joined);
    }

    [Fact]
    public async Task A_deleted_archive_is_gone_from_storage_and_answers_410()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "transfer-delete").ConfigureAwait(true);
        var (id, _) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        var key = await KeyAsync(scenario.App, id).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await TransferApi.DeleteArchiveAsync(scenario.Admin, id).ConfigureAwait(true)).StatusCode);

        await PagesApi.RefusedAsync(await TransferApi.DownloadAsync(scenario.Admin, id).ConfigureAwait(true), HttpStatusCode.Gone, "transfer.archive_gone").ConfigureAwait(true);
        Assert.Equal("expired", (await TransferApi.GetOkAsync(scenario.Admin, id).ConfigureAwait(true)).GetProperty("state").GetString());
        await using var scope = scenario.App.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IFileStorage>().ExistsAsync(new StorageKey(key!)).ConfigureAwait(true));
    }

    [Fact]
    public async Task An_export_cancelled_before_it_runs_ends_cancelled_and_stores_nothing()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "transfer-cancel").ConfigureAwait(true);
        var id = await TransferApi.StartExportOkAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Accepted, (await TransferApi.CancelAsync(scenario.Admin, id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(0, await TransferApi.RunWorkerAsync(scenario.App).ConfigureAwait(true));

        var transfer = await TransferApi.GetOkAsync(scenario.Admin, id).ConfigureAwait(true);
        Assert.Equal("cancelled", transfer.GetProperty("state").GetString());
        Assert.False(transfer.GetProperty("downloadable").GetBoolean());
        await using var scope = scenario.App.Services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        await foreach (var entry in storage.ListAsync($"{scenario.SiteA:N}/transfers/").ConfigureAwait(true))
        {
            Assert.Fail($"Nothing should be stored for a cancelled export, found {entry.Key}.");
        }

        await PagesApi.RefusedAsync(await TransferApi.CancelAsync(scenario.Admin, id).ConfigureAwait(true), HttpStatusCode.Conflict, "transfer.finished").ConfigureAwait(true);
    }

    [Fact]
    public async Task The_site_lists_its_transfers_newest_first_and_an_unknown_one_is_not_found()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "transfer-list").ConfigureAwait(true);
        var (first, _) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        var (second, _) = await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteA).ConfigureAwait(true);
        await TransferApi.ExportAsync(scenario.App, scenario.Admin, scenario.SiteB).ConfigureAwait(true);

        var list = await PagesApi.OkAsync(await TransferApi.ListAsync(scenario.Admin, scenario.SiteA).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal([second, first], list.EnumerateArray().Select(TransferApi.Id));
        Assert.All(list.EnumerateArray(), t => Assert.False(string.IsNullOrEmpty(t.GetProperty("startedBy").GetProperty("displayName").GetString())));
        await PagesApi.RefusedAsync(await TransferApi.GetAsync(scenario.Admin, Guid.NewGuid()).ConfigureAwait(true), HttpStatusCode.NotFound, "transfer.not_found")
            .ConfigureAwait(true);
    }

    private static async Task<string?> KeyAsync(QuickBuilderApp app, Guid transferId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SitesDbContext>().TransferJobs
            .Where(j => j.Id == transferId).Select(j => j.ArchiveKey).SingleAsync().ConfigureAwait(false);
    }
}
