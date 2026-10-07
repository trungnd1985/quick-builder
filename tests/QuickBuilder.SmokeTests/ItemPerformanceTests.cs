using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 015 SC-003 and the plan's performance goals, on the reference setup (2 CPUs, 4 GB) through
/// the built image:
/// <list type="bullet">
/// <item>at 10,000 items, the list, a search, each filter and sort, and a picker search answer
/// within 1 s for 95% of requests;</item>
/// <item>saving and publishing one item take under 300 ms for 95% of requests;</item>
/// <item>a schema change over 10,000 items holding three kept versions each, and 100 pending
/// scheduled publishes, still completes within 014 SC-003's 30 s (research IT-12).</item>
/// </list>
/// </summary>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class ItemPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int Items = 10_000;
    private const int Repeats = 20;

    private static readonly TimeSpan ListTarget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WriteTarget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MigrationTarget = TimeSpan.FromSeconds(30);

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
    public async Task Lists_searches_filters_and_sorts_ten_thousand_items_within_1_s_and_writes_within_300_ms()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = await SignedInAsync().ConfigureAwait(true);
        var (siteId, collectionId, schemaRevision) = await CollectionAsync(client, "Item list perf", "items.perf.test").ConfigureAwait(true);
        await SeedAsync(collectionId, siteId, versionsEach: 1).ConfigureAwait(true);

        var items = $"/api/v1/cms/sites/{siteId}/collections/{collectionId}/items";
        var queries = new Dictionary<string, string>
        {
            ["list"] = string.Empty,
            ["search"] = "?q=item%209999",
            ["status"] = "?status=published",
            ["option filter"] = "?field[f3]=o2",
            ["boolean filter"] = "?field[f4]=false",
            ["number sort"] = "?sort=field.f2&dir=asc",
            ["date sort"] = "?sort=field.f5&dir=desc",
            ["name sort, last page"] = "?sort=name&page=100&pageSize=100",
            ["picker search"] = "?q=item%2042&pageSize=25",
        };

        foreach (var (label, query) in queries)
        {
            var p95 = await P95Async(async () =>
            {
                using var response = await client.GetAsync(new Uri(items + query, UriKind.Relative)).ConfigureAwait(false);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(true);
            output.WriteLine($"{label}: p95 {p95.TotalMilliseconds:0} ms");
            Assert.True(p95 <= ListTarget, $"SC-003: {label} took {p95.TotalMilliseconds:0} ms at p95, above {ListTarget.TotalMilliseconds} ms.");
        }

        // One item, saved and published again and again: each write goes through the gate,
        // validation and the indexer.
        using var createdResponse = await client.PostAsJsonAsync(new Uri(items, UriKind.Relative), new
        {
            slug = "measured",
            schemaRevision,
            values = new { schemaVersion = 1, values = new { f1 = "Measured", f2 = 7, f3 = "o1" } },
        }).ConfigureAwait(true);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var itemId = created.GetProperty("id").GetGuid();
        var revision = created.GetProperty("revision").GetInt32();
        var counter = 0;

        var saveP95 = await P95Async(async () =>
        {
            using var response = await client.PutAsJsonAsync(new Uri($"{items}/{itemId}", UriKind.Relative), new
            {
                slug = "measured",
                schemaRevision,
                revision,
                values = new { schemaVersion = 1, values = new { f1 = $"Measured {++counter}", f2 = 7, f3 = "o1" } },
            }).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            revision = (await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("revision").GetInt32();
        }).ConfigureAwait(true);

        var publishP95 = await P95Async(async () =>
        {
            using var response = await client.PostAsJsonAsync(new Uri($"{items}/{itemId}/publish", UriKind.Relative), new { revision, cancelSchedule = false }).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            revision = (await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("revision").GetInt32();
        }).ConfigureAwait(true);

        output.WriteLine($"save: p95 {saveP95.TotalMilliseconds:0} ms; publish: p95 {publishP95.TotalMilliseconds:0} ms");
        Assert.True(saveP95 <= WriteTarget, $"Plan goal: a save took {saveP95.TotalMilliseconds:0} ms at p95, above {WriteTarget.TotalMilliseconds} ms.");
        Assert.True(publishP95 <= WriteTarget, $"Plan goal: a publish took {publishP95.TotalMilliseconds:0} ms at p95, above {WriteTarget.TotalMilliseconds} ms.");
    }

    [SkippableFact]
    public async Task A_schema_change_over_ten_thousand_items_with_their_versions_completes_within_30_s()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = await SignedInAsync().ConfigureAwait(true);
        var (siteId, collectionId, _) = await CollectionAsync(client, "Item migration perf", "migrate.perf.test").ConfigureAwait(true);
        await SeedAsync(collectionId, siteId, versionsEach: 3).ConfigureAwait(true);

        var path = $"/api/v1/cms/sites/{siteId}/collections/{collectionId}";
        var collection = await client.GetFromJsonAsync<JsonElement>(new Uri(path, UriKind.Relative)).ConfigureAwait(true);
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(collection.GetProperty("schema").GetRawText())!;

        // Remove "minutes" (f2, held by every item and version), and turn the date (f5) into text.
        var fields = collection.GetProperty("schema").GetProperty("fields").EnumerateArray()
            .Where(f => f.GetProperty("id").GetString() != "f2")
            .Select(f => f.GetProperty("id").GetString() == "f5"
                ? JsonSerializer.SerializeToElement(new { id = "f5", key = "day", name = "Day", help = "", required = false, type = "text", multiline = false, maxLength = 256 })
                : f)
            .ToList();
        var body = new { revision = collection.GetProperty("revision").GetInt32(), name = "Posts", slug = "posts", schema = new { schemaVersion = 1, titleFieldId = "f1", fields } };

        using var previewResponse = await client.PostAsJsonAsync(new Uri($"{path}/preview", UriKind.Relative), body).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var digest = (await previewResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("digest").GetString();

        var watch = Stopwatch.StartNew();
        using var save = await client.PutAsJsonAsync(new Uri(path, UriKind.Relative), new
        {
            body.revision,
            body.name,
            body.slug,
            body.schema,
            confirmation = new { digest },
        }).ConfigureAwait(true);
        watch.Stop();
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        output.WriteLine($"{Items} items, {Items * 3} versions, 100 schedules: change took {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.Elapsed <= MigrationTarget, $"Research IT-12: the change took {watch.ElapsedMilliseconds} ms, above {MigrationTarget.TotalMilliseconds} ms.");
    }

    private static async Task<TimeSpan> P95Async(Func<Task> call)
    {
        await call().ConfigureAwait(false);
        var times = new List<TimeSpan>(Repeats);
        for (var n = 0; n < Repeats; n++)
        {
            var watch = Stopwatch.StartNew();
            await call().ConfigureAwait(false);
            times.Add(watch.Elapsed);
        }

        times.Sort();
        return times[(int)Math.Ceiling(Repeats * 0.95) - 1];
    }

    private async Task SeedAsync(Guid collectionId, Guid siteId, int versionsEach)
    {
        var values =
            "jsonb_build_object('schemaVersion', 1, 'values', jsonb_build_object('f1', 'Item ' || g.n, 'f2', g.n % 60, " +
            "'f3', CASE WHEN g.n % 2 = 0 THEN 'o1' ELSE 'o2' END, 'f4', g.n % 3 = 0, 'f5', to_char(date '2026-01-01' + (g.n % 300), 'YYYY-MM-DD')))";
        var sql = ItemSeedSql.Items(siteId, collectionId, Items, values, publishEvery: 2);

        if (versionsEach > 1)
        {
            // Earlier kept versions for every item, and 100 pending scheduled publishes.
            sql +=
                " INSERT INTO cms.item_versions (item_id, number, site_id, \\\"values\\\", slug, kind, published_by, published_at_utc) " +
                $"SELECT i.id, v.n, i.site_id, i.draft, i.slug, 'publish', i.created_by, now() FROM cms.items i CROSS JOIN generate_series(2, {versionsEach}) AS v(n) " +
                $"WHERE i.collection_id = '{collectionId}' ON CONFLICT DO NOTHING; " +
                "INSERT INTO cms.item_schedules (id, site_id, item_id, action, due_at_utc, revision, state, captured_values, captured_slug, created_by, created_at_utc) " +
                $"SELECT gen_random_uuid(), site_id, id, 'publish', now() + interval '1 day', 1, 'pending', draft, slug, created_by, now() FROM cms.items WHERE collection_id = '{collectionId}' LIMIT 100;";
        }

        var seed = await _deployment.ComposeAsync($"exec -T db psql -U quickbuilder -d quickbuilder -v ON_ERROR_STOP=1 -c \"{sql}\"").ConfigureAwait(false);
        Assert.True(seed.ExitCode == 0, $"Seeding {Items} items failed:\n{seed.Output}");
    }

    private static async Task<(Guid SiteId, Guid CollectionId, int SchemaRevision)> CollectionAsync(HttpClient client, string siteName, string hostName)
    {
        using var site = await client.PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = siteName, hostNames = new[] { hostName } }).ConfigureAwait(false);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
        var collections = $"/api/v1/cms/sites/{siteId}/collections";

        using var created = await client.PostAsJsonAsync(new Uri(collections, UriKind.Relative), new { name = "Posts", slug = "posts" }).ConfigureAwait(false);
        var collectionId = (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();

        var schema = new
        {
            schemaVersion = 1,
            fields = new object[]
            {
                new { id = "~1", key = "title", name = "Title", help = "", required = true, type = "text", multiline = false, maxLength = 256 },
                new { id = "~2", key = "minutes", name = "Minutes", help = "", required = false, type = "number", decimals = 0 },
                new { id = "~3", key = "category", name = "Category", help = "", required = false, type = "option", options = new[] { new { id = "~6", label = "News" }, new { id = "~7", label = "Guides" } } },
                new { id = "~4", key = "featured", name = "Featured", help = "", required = false, type = "boolean" },
                new { id = "~5", key = "day", name = "Day", help = "", required = false, type = "date", withTime = false },
            },
        };
        using var saved = await client.PutAsJsonAsync(new Uri($"{collections}/{collectionId}", UriKind.Relative), new { revision = 1, name = "Posts", slug = "posts", schema }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var schemaRevision = (await saved.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("schemaRevision").GetInt32();
        return (siteId, collectionId, schemaRevision);
    }

    private async Task<HttpClient> SignedInAsync()
    {
        var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        await _deployment.WaitForReadyAsync(client).ConfigureAwait(false);

        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Items Perf Operator", email = "items-perf@smoke.test", password = "items performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
        return client;
    }
}
