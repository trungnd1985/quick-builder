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
/// 017 SC-004 and SC-006 (tasks T107): with 10,000 redirects, redirect responses stay under
/// 20 ms at p95 and pages keep their cache-hit budget; with 100,000 published item pages, each
/// sitemap file builds in under 2 s on a miss and is served in under 50 ms on a hit.
/// </summary>
/// <remarks>
/// Measured against the built image on the reference compose setup, from the test process, so the
/// figures include the HTTP round trip. Redirects arrive through the CSV import, two files of
/// 5,000; items are seeded in SQL as the item performance tests do, under a live template. A
/// sitemap miss follows a page publish, which moves the site's sitemap generation.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class SeoPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "seo.perf.test";
    private const int Redirects = 10_000;
    private const int Items = 100_000;
    private const int Samples = 300;
    private const int SitemapMisses = 10;

    private static readonly TimeSpan RedirectBudget = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan PageHitBudget = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan SitemapMissBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SitemapHitBudget = TimeSpan.FromMilliseconds(50);

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
    public async Task Redirects_and_sitemaps_stay_within_budget_at_scale()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(ComposeDeployment.BaseAddress),
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        var (siteId, collectionId) = await SiteAsync(client).ConfigureAwait(true);
        var root = $"/api/v1/pages/sites/{siteId}";

        // A live page, and a live template under it with 100,000 published items.
        var blog = await PageAsync(client, root, "blog", parentId: null).ConfigureAwait(true);
        var blogVersion = await PublishAsync(client, $"{root}/pages/{blog}").ConfigureAwait(true);
        var template = await PageAsync(client, root, "post", blog).ConfigureAwait(true);
        using (var set = await client.PutAsJsonAsync(
                   new Uri($"{root}/pages/{template}/template", UriKind.Relative),
                   new { collectionId, version = await VersionAsync(client, root, template).ConfigureAwait(true) }).ConfigureAwait(true))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        await PublishAsync(client, $"{root}/pages/{template}").ConfigureAwait(true);
        var seed = await _deployment.ComposeAsync(
                $"exec -T db psql -U quickbuilder -d quickbuilder -v ON_ERROR_STOP=1 -c \"{ItemSeedSql.Items(siteId, collectionId, Items, "jsonb_build_object('schemaVersion', 1, 'values', jsonb_build_object('f1', 'Item ' || g.n))", publishEvery: 1)}\"")
            .ConfigureAwait(true);
        Assert.True(seed.ExitCode == 0, $"Seeding {Items} items failed:\n{seed.Output}");

        // 10,000 redirects: nine in ten exact, one in ten prefix.
        for (var start = 1; start <= Redirects; start += 5000)
        {
            var csv = new StringBuilder("source,target,kind,match,note\n");
            for (var n = start; n < start + 5000; n++)
            {
                csv.Append(n % 10 == 0 ? $"/old-section-{n},/blog,301,prefix,\n" : $"/old-{n}.html,/blog,301,exact,\n");
            }

            using var body = new ByteArrayContent(Encoding.UTF8.GetBytes(csv.ToString()));
            body.Headers.ContentType = new("text/csv");
            using var imported = await client.PostAsync(new Uri($"{root}/redirects/import", UriKind.Relative), body).ConfigureAwait(true);
            var reply = await imported.Content.ReadAsStringAsync().ConfigureAwait(true);
            Assert.True(imported.StatusCode == HttpStatusCode.OK, $"Importing redirects failed: {reply}");
            Assert.Equal(5000, JsonElement.Parse(reply).GetProperty("added").GetInt32());
        }

        var exact = await TimeAsync(client, n => $"/old-{(n % 1000 * 10) + 1}.html", HttpStatusCode.MovedPermanently).ConfigureAwait(true);
        var prefix = await TimeAsync(client, n => $"/old-section-{(n % 1000 + 1) * 10}/deep/page", HttpStatusCode.MovedPermanently).ConfigureAwait(true);
        var page = await TimeAsync(client, _ => "/blog", HttpStatusCode.OK).ConfigureAwait(true);

        // Sitemap: three files of up to 50,000 behind the index. A miss follows a publish.
        var sitemapMisses = new List<TimeSpan>();
        for (var run = 0; run < SitemapMisses; run++)
        {
            await PublishVersionAsync(client, $"{root}/pages/{blog}", blogVersion).ConfigureAwait(true);
            foreach (var file in new[] { "/sitemap.xml", "/sitemap-1.xml", "/sitemap-2.xml", "/sitemap-3.xml" })
            {
                var timer = Stopwatch.StartNew();
                await VisitAsync(client, file, HttpStatusCode.OK).ConfigureAwait(true);
                sitemapMisses.Add(timer.Elapsed);
            }
        }

        var sitemapHit = await TimeAsync(client, n => n % 2 == 0 ? "/sitemap-1.xml" : "/sitemap.xml", HttpStatusCode.OK).ConfigureAwait(true);
        var sitemapMiss = sitemapMisses.Max();

        output.WriteLine($"exact redirect p95 {Ms(exact)}, prefix redirect p95 {Ms(prefix)}, page hit p95 {Ms(page)} (10,000 redirects)");
        output.WriteLine($"sitemap file miss max {Ms(sitemapMiss)}, hit p95 {Ms(sitemapHit)} (100,000 item pages)");

        Assert.True(exact <= RedirectBudget, $"SC-004: an exact redirect answered in {Ms(exact)} at p95.");
        Assert.True(prefix <= RedirectBudget, $"SC-004: a prefix redirect answered in {Ms(prefix)} at p95.");
        Assert.True(page <= PageHitBudget, $"SC-004: a cached page answered in {Ms(page)} at p95 with 10,000 redirects.");
        Assert.True(sitemapMiss <= SitemapMissBudget, $"SC-006: a sitemap file built in {Ms(sitemapMiss)}.");
        Assert.True(sitemapHit <= SitemapHitBudget, $"SC-006: a sitemap file was served in {Ms(sitemapHit)} at p95.");
    }

    private static async Task<TimeSpan> TimeAsync(HttpClient client, Func<int, string> path, HttpStatusCode expected)
    {
        // Warm the process once: the first request pays for JIT, not for the work measured.
        await VisitAsync(client, path(0), expected).ConfigureAwait(false);

        var samples = new List<TimeSpan>();
        for (var n = 1; n <= Samples; n++)
        {
            var timer = Stopwatch.StartNew();
            await VisitAsync(client, path(n), expected).ConfigureAwait(false);
            samples.Add(timer.Elapsed);
        }

        var sorted = samples.Order().ToList();
        return sorted[(int)Math.Ceiling(sorted.Count * 0.95) - 1];
    }

    private static async Task<(Guid SiteId, Guid CollectionId)> SiteAsync(HttpClient client)
    {
        using var site = await client.PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "SEO perf", hostNames = new[] { Host } }).ConfigureAwait(false);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
        var collections = $"/api/v1/cms/sites/{siteId}/collections";

        using var created = await client.PostAsJsonAsync(new Uri(collections, UriKind.Relative), new { name = "Posts", slug = "posts" }).ConfigureAwait(false);
        var collection = await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var collectionId = collection.GetProperty("id").GetGuid();
        var schema = new
        {
            schemaVersion = 1,
            fields = new object[] { new { id = "~1", key = "title", name = "Title", help = "", required = true, type = "text", multiline = false, maxLength = 100 } },
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

    /// <summary>Saves a heading document as the draft and publishes it; returns the draft version.</summary>
    private static async Task<uint> PublishAsync(HttpClient client, string subject)
    {
        var current = await client.GetFromJsonAsync<JsonElement>(new Uri($"{subject}/draft", UriKind.Relative)).ConfigureAwait(false);
        const string document = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":[{"id":"h1","type":"heading","props":{"text":"Perf","level":1},"children":[]}]}]}}""";
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

    private static async Task VisitAsync(HttpClient client, string path, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = Host;
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(expected, response.StatusCode);
        await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private async Task ClaimAsync(HttpClient client)
    {
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "SEO Perf Operator", email = "seo-perf@smoke.test", password = "seo performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms";
}
