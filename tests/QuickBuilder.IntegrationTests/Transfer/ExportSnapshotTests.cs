using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// An export is one point in time, and editing goes on while it runs (020 FR-005, FR-006;
/// research ST-04).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExportSnapshotTests(DatabaseFixture database)
{
    [Fact]
    public async Task Changes_committed_while_the_parts_export_are_not_in_the_archive_and_are_never_refused()
    {
        using var site = await FullSite.CreateAsync(database, "export-snapshot").ConfigureAwait(true);
        var aboutPath = PagesApi.PagePath(site.Site, site.About);
        HttpStatusCode? publishDuringExport = null;

        // A second instance on the same database and storage, with a part that runs between
        // Cms and Pages and changes the about page through the first instance meanwhile.
        using var exporter = new QuickBuilderApp(
            site.App.ConnectionString,
            storagePath: site.App.StoragePath,
            configureTestServices: services => services.AddScoped<ISiteArchivePart>(_ => new HookPart(async () =>
            {
                await PublishingApi.SaveAsync(site.Admin, aboutPath, PublishingApi.HeadingDocument("Changed during the export", site.Header)).ConfigureAwait(false);
                var response = await PublishingApi.PublishAsync(site.Admin, aboutPath, await PublishingApi.DraftVersionAsync(site.Admin, aboutPath).ConfigureAwait(false))
                    .ConfigureAwait(false);
                publishDuringExport = response.StatusCode;
            })));

        var jobId = await StartExportAsync(exporter, site.Site).ConfigureAwait(true);
        await TransferApi.RunWorkerAsync(exporter).ConfigureAwait(true);

        // FR-006: the edit went through while the export ran.
        Assert.Equal(HttpStatusCode.OK, publishDuringExport);
        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(site.Admin, aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal([1, 2, 3], history.GetProperty("retainedVersions").EnumerateArray().Select(v => v.GetInt32()).Order());

        // FR-005: the archive holds the about page as it was when the export began, whole.
        var download = await TransferApi.DownloadAsync(site.Admin, jobId).ConfigureAwait(true);
        download.EnsureSuccessStatusCode();
        await using var archive = await TransferApi.OpenAsync(await download.Content.ReadAsByteArrayAsync().ConfigureAwait(true)).ConfigureAwait(true);

        var versions = (await TransferApi.LinesAsync(archive, "pages", "versions").ConfigureAwait(true))
            .Where(v => v.GetProperty("subjectId").GetGuid() == site.About)
            .Select(v => v.GetProperty("number").GetInt32());
        Assert.Equal([1, 2], versions);
        var draft = (await TransferApi.LinesAsync(archive, "pages", "page-drafts").ConfigureAwait(true)).Single(d => d.GetProperty("subjectId").GetGuid() == site.About);
        Assert.Contains("About Acme (draft)", draft.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Changed during the export", draft.GetRawText(), StringComparison.Ordinal);
        var live = (await TransferApi.LinesAsync(archive, "pages", "publications").ConfigureAwait(true)).Single(p => p.GetProperty("subjectId").GetGuid() == site.About);
        Assert.Equal(2, live.GetProperty("liveNumber").GetInt32());
    }

    private static async Task<Guid> StartExportAsync(QuickBuilderApp app, Guid siteId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var job = TransferJob.StartExport(siteId, TransferTriggers.Admin, startedBy: null, DateTimeOffset.UtcNow);
        Assert.Equal(SaveOutcome.Saved, await scope.ServiceProvider.GetRequiredService<TransferJobStore>().SaveAsync(job, null, CancellationToken.None).ConfigureAwait(false));
        return job.Id;
    }

    /// <summary>A part that writes nothing and runs a callback, between Cms (40) and Pages (50).</summary>
    private sealed class HookPart(Func<Task> during) : ISiteArchivePart
    {
        public string Name => "hook";

        public int Order => 45;

        public int Version => 1;

        public IReadOnlyDictionary<string, int> Contracts { get; } = new Dictionary<string, int>();

        public Task ExportAsync(PartExportContext context, CancellationToken cancellationToken) => during();

        public Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
