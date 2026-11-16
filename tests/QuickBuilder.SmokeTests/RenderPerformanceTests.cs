using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 012 SC-003, SC-004, FR-041: a published 200-block page with a header and a footer renders in
/// under 200 ms at p95 when it is not cached, and is served in under 20 ms at p95 when it is,
/// with no JavaScript in it.
/// </summary>
/// <remarks>
/// Measured against the built image on the reference compose setup, from the test process,
/// so the figures include the HTTP round trip a visitor would pay. Each miss follows a
/// republish, which moves the page's generation on and so gives it a new cache key — exactly
/// what a visitor meets after an editor publishes. The page is
/// <c>tests/fixtures/rendering/page-200-blocks.json</c>, which the golden runner also checks.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class RenderPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "render.perf.test";
    private const int Misses = 200;
    private const int Hits = 1000;

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
    public async Task A_200_block_page_renders_within_budget_and_is_served_from_the_cache_faster_still()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Render perf", hostNames = new[] { Host } })
            .ConfigureAwait(true);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var root = $"/api/v1/pages/sites/{siteId}";

        // The fixture's header and footer ids stand for components created here.
        using var context = JsonDocument.Parse(await File.ReadAllTextAsync(Fixture("context.json")).ConfigureAwait(true));
        var document = await File.ReadAllTextAsync(Fixture("page-200-blocks.json")).ConfigureAwait(true);
        foreach (var component in context.RootElement.GetProperty("components").EnumerateObject())
        {
            var id = await CreateComponentAsync(client, root, component.Value.GetRawText()).ConfigureAwait(true);
            document = document.Replace(component.Name, id.ToString(), StringComparison.Ordinal);
        }

        using var created = await client
            .PostAsJsonAsync(new Uri($"{root}/pages", UriKind.Relative), new { title = "Performance", slug = "performance" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var page = $"{root}/pages/{(await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid()}";
        var draftVersion = await SaveAsync(client, page, document).ConfigureAwait(true);
        await PublishAsync(client, page, draftVersion).ConfigureAwait(true);

        // Warm the process once: the first request pays for JIT, not for rendering.
        var first = await VisitAsync(client).ConfigureAwait(true);
        Assert.Equal(0, CountOccurrences(first, "<script"));

        var misses = new List<TimeSpan>();
        for (var run = 0; run < Misses; run++)
        {
            await PublishAsync(client, page, draftVersion).ConfigureAwait(true);
            var timer = Stopwatch.StartNew();
            await VisitAsync(client).ConfigureAwait(true);
            misses.Add(timer.Elapsed);
        }

        var hits = new List<TimeSpan>();
        for (var run = 0; run < Hits; run++)
        {
            var timer = Stopwatch.StartNew();
            await VisitAsync(client).ConfigureAwait(true);
            hits.Add(timer.Elapsed);
        }

        output.WriteLine(
            $"200-block page, {first.Length} bytes: miss p50 {Ms(P(misses, 0.5))} p95 {Ms(P(misses, 0.95))}; " +
            $"hit p50 {Ms(P(hits, 0.5))} p95 {Ms(P(hits, 0.95))}; <script> bytes 0");

        Assert.True(P(misses, 0.95) <= MissBudget, $"SC-003: rendering took {Ms(P(misses, 0.95))} at p95.");
        Assert.True(P(hits, 0.95) <= HitBudget, $"SC-004: a cached page took {Ms(P(hits, 0.95))} at p95.");
    }

    private string Fixture(string name) => Path.Combine(_deployment.RepositoryRoot, "tests", "fixtures", "rendering", name);

    private static async Task<string> VisitAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/performance", UriKind.Relative));
        request.Headers.Host = Host;
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static async Task<Guid> CreateComponentAsync(HttpClient client, string root, string document)
    {
        using var created = await client
            .PostAsJsonAsync(new Uri($"{root}/components", UriKind.Relative), new { name = $"Part {Guid.NewGuid():N}" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();

        var path = $"{root}/components/{id}";
        var version = await SaveAsync(client, path, document).ConfigureAwait(false);
        await PublishAsync(client, path, version).ConfigureAwait(false);
        return id;
    }

    /// <summary>Saves over the current draft and returns the new draft version.</summary>
    private static async Task<uint> SaveAsync(HttpClient client, string subject, string document)
    {
        using var current = await client.GetAsync(new Uri($"{subject}/draft", UriKind.Relative)).ConfigureAwait(false);
        var version = (await current.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("version").GetUInt32();

        using var body = new StringContent($$"""{"version":{{version}},"document":{{document}}}""", System.Text.Encoding.UTF8, "application/json");
        using var saved = await client.PutAsync(new Uri($"{subject}/draft", UriKind.Relative), body).ConfigureAwait(false);
        var reply = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, $"Saving a draft failed: {reply}");
        return JsonElement.Parse(reply).GetProperty("version").GetUInt32();
    }

    private static async Task PublishAsync(HttpClient client, string subject, uint draftVersion)
    {
        using var published = await client
            .PostAsJsonAsync(new Uri($"{subject}/publish", UriKind.Relative), new { draftVersion })
            .ConfigureAwait(false);
        Assert.True(published.StatusCode == HttpStatusCode.OK, $"Publishing failed: {await published.Content.ReadAsStringAsync().ConfigureAwait(false)}");
    }

    private async Task ClaimAsync(HttpClient client)
    {
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Render Perf Operator", email = "render-perf@smoke.test", password = "render performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    private static TimeSpan P(IEnumerable<TimeSpan> samples, double percentile)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * percentile) - 1)];
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms";
}
