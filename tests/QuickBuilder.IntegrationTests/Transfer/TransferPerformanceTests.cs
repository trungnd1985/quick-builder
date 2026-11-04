using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Transfer.Fixtures;
using Xunit.Abstractions;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// The size SC-002 names: export and import of 500 pages, 10,000 items and 2 GB of media, with
/// visitors' page times measured while each runs (020 SC-002, research ST-24).
/// </summary>
/// <remarks>
/// Manual: it runs only with <c>QB_TRANSFER_PERF=1</c>, and its numbers mean something only on
/// the reference setup (2 vCPU, 4 GB RAM). <c>QB_TRANSFER_PERF_SCALE</c> (default <c>1</c>)
/// multiplies the sizes, so a smaller run can prove the test itself works. Results go to
/// <c>specs/020-site-export-import/quickstart-results.md</c>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Performance")]
public sealed class TransferPerformanceTests(DatabaseFixture database, ITestOutputHelper output)
{
    private const int Pages = 500;
    private const int Items = 10_000;
    private const long MediaBytes = 2L * 1024 * 1024 * 1024;
    private const int FileBytes = 16 * 1024 * 1024;

    [SkippableFact]
    public async Task Export_and_import_of_the_reference_site_stay_within_their_times()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("QB_TRANSFER_PERF") == "1", "Manual: set QB_TRANSFER_PERF=1 on the reference setup.");
        var scale = double.Parse(Environment.GetEnvironmentVariable("QB_TRANSFER_PERF_SCALE") ?? "1", CultureInfo.InvariantCulture);

        using var site = await FullSite.CreateAsync(database, "transfer-perf").ConfigureAwait(true);
        var seeded = Stopwatch.StartNew();
        await SeedAsync(site, (int)(Pages * scale), (int)(Items * scale), (long)(MediaBytes * scale)).ConfigureAwait(true);
        output.WriteLine($"seeded in {seeded.Elapsed}");

        // The full-size archive is over 2 GB, more than one array holds: it goes through a file.
        var archive = Path.Combine(Path.GetTempPath(), $"transfer-perf-{Guid.NewGuid():N}.qbsite");
        try
        {
            var visitor = site.App.CreateClientForHost("cms-a.test");

            // The first request renders cold; then the same page with nothing else running is the baseline.
            using (await visitor.GetAsync(new Uri("/about", UriKind.Relative)).ConfigureAwait(true))
            {
            }

            var idle = await TimedAsync(visitor, async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(true);
            output.WriteLine($"idle: visitor p50 {idle.P50} ms, p95 {idle.P95} ms over {idle.Requests} requests");

            // The export job, then the download, the upload and the import, each timed on its own:
            // in the test the archive moves through an in-memory server at memory speed, which
            // loads the CPUs far more than a network does, so it is kept apart from the jobs.
            var export = await TimedAsync(visitor, () => TransferApi.RunExportAsync(site.App, site.Admin, site.Site)).ConfigureAwait(true);
            output.WriteLine($"export: {export.Elapsed}, visitor p50 {export.P50} ms, p95 {export.P95} ms over {export.Requests} requests");

            var download = await TimedAsync(visitor, () => TransferApi.DownloadToFileAsync(site.Admin, export.Value, archive)).ConfigureAwait(true);
            output.WriteLine($"download: {download.Elapsed}, {download.Value:N0} bytes, visitor p50 {download.P50} ms, p95 {download.P95} ms over {download.Requests} requests");

            var upload = await TimedAsync(visitor, () => TransferApi.UploadFileAsync(site.Admin, archive)).ConfigureAwait(true);
            output.WriteLine($"upload: {upload.Elapsed}, visitor p50 {upload.P50} ms, p95 {upload.P95} ms over {upload.Requests} requests");

            var import = await TimedAsync(visitor, () => TransferApi.ImportUploadedAsNewSiteAsync(site.App, site.Admin, upload.Value, "Performance copy", hostName: "perf-copy.test")).ConfigureAwait(true);
            output.WriteLine($"import: {import.Elapsed}, visitor p50 {import.P50} ms, p95 {import.P95} ms over {import.Requests} requests");
            output.WriteLine($"peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024):N0} MB");

            if (scale >= 1)
            {
                Assert.True(export.Elapsed < TimeSpan.FromMinutes(15), $"Export took {export.Elapsed}.");
                Assert.True(import.Elapsed < TimeSpan.FromMinutes(20), $"Import took {import.Elapsed}.");
            }
        }
        finally
        {
            File.Delete(archive);
        }
    }

    private static async Task SeedAsync(FullSite site, int pages, int items, long mediaBytes)
    {
        for (var i = 0; i < pages; i++)
        {
            var id = (await PagesApi.CreateOkAsync(site.Admin, site.Site, $"perf-{i}").ConfigureAwait(false)).GetProperty("id").GetGuid();
            var path = PagesApi.PagePath(site.Site, id);
            await PublishingApi.SaveAsync(site.Admin, path, PublishingApi.HeadingDocument($"Page {i}")).ConfigureAwait(false);
            await PublishingApi.PublishCurrentAsync(site.Admin, path).ConfigureAwait(false);
        }

        var revision = site.World.Items.PostsSchemaRevision;
        for (var i = 0; i < items; i++)
        {
            await ItemsApi.CreateOkAsync(site.Admin, site.Site, site.World.Posts,
                ItemsApi.Draft($"perf-{i}", new JsonObject { ["f1"] = $"Post {i}" }, revision)).ConfigureAwait(false);
        }

        // PDFs padded to 16 MB: the upload rules read the head, the archive carries every byte.
        var head = Encoding.ASCII.GetBytes("%PDF-1.4\n");
        for (long written = 0, n = 0; written < mediaBytes; written += FileBytes, n++)
        {
            var bytes = new byte[FileBytes];
            head.CopyTo(bytes, 0);
            Random.Shared.NextBytes(bytes.AsSpan(head.Length));
            await MediaApi.UploadOkAsync(site.Admin, site.Site, bytes, "pdf", "application/pdf").ConfigureAwait(false);
        }
    }

    /// <summary>Runs <paramref name="work"/> while a visitor keeps asking for a page; returns the time and the visitor's p50 and p95.</summary>
    /// <remarks>
    /// It also samples the thread pool and the garbage collector every 50 ms and writes them out
    /// with each visitor request over 100 ms, which tells thread-pool starvation from GC pauses.
    /// </remarks>
    private async Task<(T Value, TimeSpan Elapsed, long P50, long P95, int Requests)> TimedAsync<T>(HttpClient visitor, Func<Task<T>> work)
    {
        using var stop = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var visitsMade = new List<(double Start, long Took)>();
        var samples = new List<(double At, int Threads, long Queued, TimeSpan Paused, int Gen2)>();
        var visits = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var start = clock.Elapsed.TotalSeconds;
                var one = Stopwatch.StartNew();
                using var response = await visitor.GetAsync(new Uri("/about", UriKind.Relative), CancellationToken.None).ConfigureAwait(false);
                visitsMade.Add((start, one.ElapsedMilliseconds));
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }
        });

        // A dedicated thread, so the sampler keeps sampling while the pool is starved.
        var sampler = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                lock (samples)
                {
                    samples.Add((clock.Elapsed.TotalSeconds, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount, GC.GetTotalPauseDuration(), GC.CollectionCount(2)));
                }

                Thread.Sleep(50);
            }
        }) { IsBackground = true, Name = "perf sampler" };
        sampler.Start();

        var value = await work().ConfigureAwait(false);
        var elapsed = clock.Elapsed;
        await stop.CancelAsync().ConfigureAwait(false);
        await visits.ConfigureAwait(false);
        sampler.Join();

        var first = samples[0];
        var last = samples[^1];
        output.WriteLine($"  pool threads {first.Threads} to {samples.Max(s => s.Threads)}, queued max {samples.Max(s => s.Queued)}, " +
            $"GC paused {(last.Paused - first.Paused).TotalMilliseconds:N0} ms, gen2 {last.Gen2 - first.Gen2}");
        foreach (var (start, took) in visitsMade.Where(v => v.Took > 100))
        {
            var during = samples.Where(s => s.At >= start - 0.05 && s.At <= start + (took / 1000.0) + 0.05).ToList();
            if (during.Count == 0)
            {
                continue;
            }

            output.WriteLine($"  slow: at {start:N2} s took {took} ms; threads {during.Min(s => s.Threads)} to {during.Max(s => s.Threads)}, " +
                $"queued max {during.Max(s => s.Queued)}, GC paused {(during[^1].Paused - during[0].Paused).TotalMilliseconds:N0} ms");
        }

        var sorted = visitsMade.Select(v => v.Took).Order().ToList();
        long At(double quantile) => sorted.Count == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Count * quantile) - 1];
        return (value, elapsed, At(0.5), At(0.95), sorted.Count);
    }
}
