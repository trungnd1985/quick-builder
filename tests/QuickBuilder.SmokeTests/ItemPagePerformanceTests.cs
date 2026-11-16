using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 016 SC-004: item pages and pages with lists render in under 200 ms at p95 on a cache miss and
/// are served in under 20 ms at p95 on a hit, over 10,000 published items (tasks T092).
/// </summary>
/// <remarks>
/// Measured against the built image on the reference compose setup, from the test process, so the
/// figures include the HTTP round trip. Each miss follows a republish of the page (or the
/// template), which gives it a new cache key, as a visitor meets after an editor publishes. List
/// queries per render are fixed by the page's shape (research TB-09) and are reported with the
/// timings: one per list, and one per distinct outer value for a nested list.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class ItemPagePerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "items.perf.test";
    private const int Items = 10_000;
    private const int Misses = 100;
    private const int Hits = 500;

    private static readonly TimeSpan MissBudget = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HitBudget = TimeSpan.FromMilliseconds(20);

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
    public async Task Item_pages_and_list_pages_render_within_budget_over_ten_thousand_items()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        var (siteId, collectionId) = await CollectionAsync(client).ConfigureAwait(true);
        var values =
            "jsonb_build_object('schemaVersion', 1, 'values', jsonb_build_object('f1', 'Item ' || g.n, 'f2', g.n % 50))";
        var seed = await _deployment.ComposeAsync(
                $"exec -T db psql -U quickbuilder -d quickbuilder -v ON_ERROR_STOP=1 -c \"{ItemSeedSql.Items(siteId, collectionId, Items, values, publishEvery: 1)}\"")
            .ConfigureAwait(true);
        Assert.True(seed.ExitCode == 0, $"Seeding {Items} items failed:\n{seed.Output}");

        var root = $"/api/v1/pages/sites/{siteId}";
        var blog = await PageAsync(client, root, "blog", parentId: null).ConfigureAwait(true);
        var template = await PageAsync(client, root, "post", blog).ConfigureAwait(true);
        using (var set = await client.PutAsJsonAsync(
                   new Uri($"{root}/pages/{template}/template", UriKind.Relative),
                   new { collectionId, version = await VersionAsync(client, root, template).ConfigureAwait(true) }).ConfigureAwait(true))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        // An item page of 200 blocks: 180 bound headings and a list of 20 posts.
        var itemBlocks = Enumerable.Range(1, 180).Select(n => Heading($"h{n}", """{"field":"f1"}""")).ToList();
        itemBlocks.Add(List("related", collectionId, """ "filters":[{"field":"f2","op":"eq","value":{"field":"f2","scope":"page"}}],"limit":20 """, Heading("rt", """{"field":"f1"}""")));
        var itemPage = await PublishAsync(client, $"{root}/pages/{template}", Document(itemBlocks)).ConfigureAwait(true);

        // A paginated list of 100 per page.
        var paged = await PageAsync(client, root, "all", parentId: null).ConfigureAwait(true);
        var pagedVersion = await PublishAsync(client, $"{root}/pages/{paged}", Document([
            List("all", collectionId, """ "sort":[{"field":"f2","dir":"desc"},{"item":"slug","dir":"asc"}],"paging":{"pageSize":100} """, Heading("at", """{"field":"f1"}""")),
        ])).ConfigureAwait(true);

        // A nested list: 20 posts, each with 2 posts of the same group.
        var nested = await PageAsync(client, root, "groups", parentId: null).ConfigureAwait(true);
        var nestedVersion = await PublishAsync(client, $"{root}/pages/{nested}", Document([
            List("outer", collectionId, """ "sort":[{"item":"slug","dir":"asc"}],"limit":20 """,
                Heading("ot", """{"field":"f1"}"""),
                List("inner", collectionId, """ "filters":[{"field":"f2","op":"eq","value":{"field":"f2","scope":"outer"}}],"limit":2 """, Heading("it", """{"field":"f1"}"""))),
        ])).ConfigureAwait(true);

        var results = new[]
        {
            await MeasureAsync(client, "item page, 200 blocks, list of 20 (2 list queries)", "/blog/item-4321", $"{root}/pages/{template}", itemPage).ConfigureAwait(true),
            await MeasureAsync(client, "paginated list of 100, page 50 (1 list query)", "/all?page=50", $"{root}/pages/{paged}", pagedVersion).ConfigureAwait(true),
            await MeasureAsync(client, "nested list 20 x 2 (1 + up to 20 list queries)", "/groups", $"{root}/pages/{nested}", nestedVersion).ConfigureAwait(true),
        };

        foreach (var (name, miss, hit) in results)
        {
            output.WriteLine($"{name}: miss p95 {Ms(miss)}, hit p95 {Ms(hit)}");
        }

        foreach (var (name, miss, hit) in results)
        {
            Assert.True(miss <= MissBudget, $"SC-004: {name} rendered in {Ms(miss)} at p95.");
            Assert.True(hit <= HitBudget, $"SC-004: {name} was served from the cache in {Ms(hit)} at p95.");
        }
    }

    private static async Task<(string Name, TimeSpan Miss, TimeSpan Hit)> MeasureAsync(
        HttpClient client, string name, string path, string subject, uint draftVersion)
    {
        // Warm the process once: the first request pays for JIT, not for rendering.
        await VisitAsync(client, path).ConfigureAwait(false);

        var misses = new List<TimeSpan>();
        for (var run = 0; run < Misses; run++)
        {
            await PublishVersionAsync(client, subject, draftVersion).ConfigureAwait(false);
            var timer = Stopwatch.StartNew();
            await VisitAsync(client, path).ConfigureAwait(false);
            misses.Add(timer.Elapsed);
        }

        var hits = new List<TimeSpan>();
        for (var run = 0; run < Hits; run++)
        {
            var timer = Stopwatch.StartNew();
            await VisitAsync(client, path).ConfigureAwait(false);
            hits.Add(timer.Elapsed);
        }

        return (name, P(misses, 0.95), P(hits, 0.95));
    }

    private static string Document(IEnumerable<string> blocks) =>
        """{"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":["""
        + string.Join(',', blocks) + "]}]}}";

    private static string Heading(string id, string value) =>
        "{\"id\":\"" + id + "\",\"type\":\"heading\",\"props\":{\"text\":\"x\",\"level\":3,\"bind\":{\"text\":{\"value\":" + value + "}}},\"children\":[]}";

    private static string List(string id, Guid collectionId, string props, params string[] card) =>
        "{\"id\":\"" + id + "\",\"type\":\"collection-list\",\"props\":{\"source\":{\"collectionId\":\"" + collectionId + "\"}," + props
        + "},\"children\":[{\"id\":\"" + id + "-card\",\"type\":\"list-card\",\"props\":{},\"children\":[" + string.Join(',', card)
        + "]},{\"id\":\"" + id + "-empty\",\"type\":\"list-empty\",\"props\":{},\"children\":[]}]}";

    private static async Task<(Guid SiteId, Guid CollectionId)> CollectionAsync(HttpClient client)
    {
        using var site = await client.PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Item perf", hostNames = new[] { Host } }).ConfigureAwait(false);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
        var collections = $"/api/v1/cms/sites/{siteId}/collections";

        using var created = await client.PostAsJsonAsync(new Uri(collections, UriKind.Relative), new { name = "Posts", slug = "posts" }).ConfigureAwait(false);
        var collection = await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var collectionId = collection.GetProperty("id").GetGuid();
        var schema = new
        {
            schemaVersion = 1,
            fields = new object[]
            {
                new { id = "~1", key = "title", name = "Title", help = "", required = true, type = "text", multiline = false, maxLength = 100 },
                new { id = "~2", key = "group", name = "Group", help = "", required = false, type = "number", decimals = 0 },
            },
        };
        using var saved = await client.PutAsJsonAsync(
            new Uri($"{collections}/{collectionId}", UriKind.Relative),
            new { revision = collection.GetProperty("revision").GetInt32(), name = "Posts", slug = "posts", schema }).ConfigureAwait(false);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, $"Saving the schema failed: {await saved.Content.ReadAsStringAsync().ConfigureAwait(false)}");
        return (siteId, collectionId);
    }

    private static async Task<Guid> PageAsync(HttpClient client, string root, string slug, Guid? parentId)
    {
        using var created = await client.PostAsJsonAsync(new Uri($"{root}/pages", UriKind.Relative), new { title = slug, slug, parentId }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
    }

    private static async Task<int> VersionAsync(HttpClient client, string root, Guid pageId) =>
        (await client.GetFromJsonAsync<JsonElement>(new Uri($"{root}/pages/{pageId}", UriKind.Relative)).ConfigureAwait(false)).GetProperty("version").GetInt32();

    /// <summary>Saves the document as the draft and publishes it; returns the draft version.</summary>
    private static async Task<uint> PublishAsync(HttpClient client, string subject, string document)
    {
        var current = await client.GetFromJsonAsync<JsonElement>(new Uri($"{subject}/draft", UriKind.Relative)).ConfigureAwait(false);
        using var body = new StringContent(
            $$"""{"version":{{current.GetProperty("version").GetUInt32()}},"document":{{document}}}""", Encoding.UTF8, "application/json");
        using var saved = await client.PutAsync(new Uri($"{subject}/draft", UriKind.Relative), body).ConfigureAwait(false);
        var reply = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, $"Saving a draft failed: {reply}");
        var version = JsonElement.Parse(reply).GetProperty("version").GetUInt32();
        await PublishVersionAsync(client, subject, version).ConfigureAwait(false);
        return version;
    }

    private static async Task PublishVersionAsync(HttpClient client, string subject, uint draftVersion)
    {
        using var published = await client.PostAsJsonAsync(new Uri($"{subject}/publish", UriKind.Relative), new { draftVersion }).ConfigureAwait(false);
        Assert.True(published.StatusCode == HttpStatusCode.OK, $"Publishing failed: {await published.Content.ReadAsStringAsync().ConfigureAwait(false)}");
    }

    private static async Task VisitAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = Host;
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private async Task ClaimAsync(HttpClient client)
    {
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Item Perf Operator", email = "item-perf@smoke.test", password = "item page performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private static TimeSpan P(IEnumerable<TimeSpan> samples, double percentile)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * percentile) - 1)];
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms";
}
