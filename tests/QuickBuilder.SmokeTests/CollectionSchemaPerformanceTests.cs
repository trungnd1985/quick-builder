using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 014 SC-003: on a collection of 10,000 items, the preview of a change that touches every item
/// appears within 5 s, and the confirmed change completes within 30 s, on the reference setup
/// (2 CPUs, 4 GB) through the built image.
/// </summary>
/// <remarks>
/// The items are seeded with one SQL statement, as the media test seeds files: there is no item
/// API until #15 (research CS-19). The change is text to number on a field every item holds,
/// so the preview counts and the save rewrites all 10,000 items, draft and published.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class CollectionSchemaPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int Items = 10_000;

    private static readonly TimeSpan PreviewTarget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SaveTarget = TimeSpan.FromSeconds(30);

    private readonly ComposeDeployment _deployment = new("docker-compose.perf.yml");

    public async Task InitializeAsync()
    {
        if (!await _deployment.StartAsync().ConfigureAwait(false))
        {
            output.WriteLine("Docker is not available; performance test will skip.");
        }
    }

    public Task DisposeAsync() => _deployment.StopAsync();

    [SkippableFact]
    public async Task A_change_to_ten_thousand_items_previews_within_5_s_and_saves_within_30_s()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Cms perf", hostNames = new[] { "cms.perf.test" } })
            .ConfigureAwait(true);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var collections = $"/api/v1/cms/sites/{siteId}/collections";

        using var created = await client.PostAsJsonAsync(new Uri(collections, UriKind.Relative), new { name = "Posts", slug = "posts" }).ConfigureAwait(true);
        var collectionId = (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var path = $"{collections}/{collectionId}";

        var textSchema = new
        {
            schemaVersion = 1,
            fields = new object[]
            {
                new { id = "~1", key = "title", name = "Title", help = "", required = true, type = "text", multiline = false, maxLength = 256 },
                new { id = "~2", key = "minutes", name = "Minutes", help = "", required = false, type = "text", multiline = false, maxLength = 256 },
            },
        };
        using (var saved = await client.PutAsJsonAsync(new Uri(path, UriKind.Relative), new { revision = 1, name = "Posts", slug = "posts", schema = textSchema }).ConfigureAwait(true))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        var seed = await _deployment.ComposeAsync(
            "exec -T db psql -U quickbuilder -d quickbuilder -v ON_ERROR_STOP=1 -c \"" +
            "INSERT INTO cms.items (id, site_id, collection_id, draft, published, created_at_utc, updated_at_utc) " +
            "SELECT g.id, s.site, s.coll, v.doc, v.doc, now(), now() " +
            "FROM (SELECT gen_random_uuid() AS id, n FROM generate_series(1, " + Items + ") AS n) g " +
            $"CROSS JOIN (SELECT '{siteId}'::uuid AS site, '{collectionId}'::uuid AS coll) s " +
            "CROSS JOIN LATERAL (SELECT jsonb_build_object('schemaVersion', 1, 'values', " +
            "jsonb_build_object('f1', 'Item ' || g.n, 'f2', CASE WHEN g.n % 10 = 0 THEN 'about ' || g.n ELSE (g.n % 60)::text END)) AS doc) v;\"").ConfigureAwait(true);
        Assert.True(seed.ExitCode == 0, $"Seeding {Items} items failed:\n{seed.Output}");

        var numberSchema = new
        {
            schemaVersion = 1,
            titleFieldId = "f1",
            fields = new object[]
            {
                new { id = "f1", key = "title", name = "Title", help = "", required = true, type = "text", multiline = false, maxLength = 256 },
                new { id = "f2", key = "minutes", name = "Minutes", help = "", required = false, type = "number", decimals = 0 },
            },
        };

        var previewWatch = Stopwatch.StartNew();
        using var previewResponse = await client.PostAsJsonAsync(new Uri($"{path}/preview", UriKind.Relative), new { revision = 2, name = "Posts", slug = "posts", schema = numberSchema }).ConfigureAwait(true);
        previewWatch.Stop();
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var digest = preview.GetProperty("digest").GetString();

        var saveWatch = Stopwatch.StartNew();
        using var save = await client.PutAsJsonAsync(
            new Uri(path, UriKind.Relative),
            new { revision = 2, name = "Posts", slug = "posts", schema = numberSchema, confirmation = new { digest } }).ConfigureAwait(true);
        saveWatch.Stop();
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var counts = preview.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "typeChanged").GetProperty("counts");
        output.WriteLine($"{Items} items: preview {previewWatch.ElapsedMilliseconds} ms, confirmed change {saveWatch.ElapsedMilliseconds} ms; counts {counts.GetRawText()}");

        Assert.True(previewWatch.Elapsed <= PreviewTarget, $"SC-003: the preview took {previewWatch.ElapsedMilliseconds} ms, above {PreviewTarget.TotalMilliseconds} ms.");
        Assert.True(saveWatch.Elapsed <= SaveTarget, $"SC-003: the change took {saveWatch.ElapsedMilliseconds} ms, above {SaveTarget.TotalMilliseconds} ms.");
    }

    private async Task ClaimAsync(HttpClient client)
    {
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Cms Perf Operator", email = "cms-perf@smoke.test", password = "cms performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }
}
