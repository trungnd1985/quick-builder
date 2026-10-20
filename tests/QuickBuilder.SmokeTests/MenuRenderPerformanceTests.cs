using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 024 SC-005: a page showing two menus of 300 entries over three levels renders in under
/// 200 ms at p95 when it is not cached, and is served in under 20 ms at p95 when it is.
/// </summary>
/// <remarks>
/// Measured as <see cref="RenderPerformanceTests"/> measures, against the built image on the
/// reference compose setup. Each menu has 30 top-level entries linking to live pages, each with
/// 3 children, each with 2 grandchildren: 30 + 90 + 180 = 300 entries. One menu is horizontal,
/// the other vertical and collapsing, so the page carries the script and every disclosure. Each
/// miss follows a republish of the page, which gives it a new cache key.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class MenuRenderPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "menu.perf.test";
    private const int Misses = 200;
    private const int Hits = 1000;
    private const int TopLevel = 30;

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
    public async Task A_page_with_two_300_entry_menus_renders_within_budget_and_is_served_from_the_cache_faster_still()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Menu perf", hostNames = new[] { Host } })
            .ConfigureAwait(true);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var root = $"/api/v1/pages/sites/{siteId}";

        // The pages the top-level entries link to, live so every one is resolved as a link.
        var targets = new List<Guid>();
        for (var i = 0; i < TopLevel; i++)
        {
            var target = await CreatePageAsync(client, root, $"Section {i}", $"section-{i}").ConfigureAwait(true);
            await PublishAsync(client, $"{root}/pages/{target}", await SaveAsync(client, $"{root}/pages/{target}", Heading($"Section {i}")).ConfigureAwait(true)).ConfigureAwait(true);
            targets.Add(target);
        }

        var main = await CreateMenuAsync(client, root, "Main", targets).ConfigureAwait(true);
        var side = await CreateMenuAsync(client, root, "Side", targets).ConfigureAwait(true);

        var pageId = await CreatePageAsync(client, root, "Performance", "performance").ConfigureAwait(true);
        var page = $"{root}/pages/{pageId}";
        var document = $$"""
            { "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [
              { "id": "s", "type": "section", "props": {}, "children": [
                { "id": "n1", "type": "navigation", "props": { "menuId": "{{main}}", "levels": 3 }, "children": [] },
                { "id": "n2", "type": "navigation", "props": { "menuId": "{{side}}", "levels": 3, "layout": "vertical", "collapse": { "enabled": true, "below": "tablet" } }, "children": [] },
                { "id": "h", "type": "heading", "props": { "text": "Performance", "level": 1 }, "children": [] } ] } ] } }
            """;
        var draftVersion = await SaveAsync(client, page, document).ConfigureAwait(true);
        await PublishAsync(client, page, draftVersion).ConfigureAwait(true);

        // Warm the process once: the first request pays for JIT, not for rendering.
        var first = await VisitAsync(client).ConfigureAwait(true);
        Assert.Equal(2 * TopLevel * 10, CountOccurrences(first, "class=\"qb-nav__item"));

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
            $"Two 300-entry menus, {first.Length} bytes: miss p50 {Ms(P(misses, 0.5))} p95 {Ms(P(misses, 0.95))}; " +
            $"hit p50 {Ms(P(hits, 0.5))} p95 {Ms(P(hits, 0.95))}");

        Assert.True(P(misses, 0.95) <= MissBudget, $"SC-005: rendering took {Ms(P(misses, 0.95))} at p95.");
        Assert.True(P(hits, 0.95) <= HitBudget, $"SC-005: a cached page took {Ms(P(hits, 0.95))} at p95.");
    }

    /// <summary>A published menu: each target page at the top, 3 children each, 2 grandchildren each.</summary>
    private static async Task<Guid> CreateMenuAsync(HttpClient client, string root, string name, List<Guid> targets)
    {
        using var created = await client.PostAsJsonAsync(new Uri($"{root}/menus", UriKind.Relative), new { name }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var menu = await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var id = menu.GetProperty("id").GetGuid();

        var entries = new JsonArray();
        for (var i = 0; i < targets.Count; i++)
        {
            var children = new JsonArray();
            for (var j = 0; j < 3; j++)
            {
                var grandchildren = new JsonArray();
                for (var k = 0; k < 2; k++)
                {
                    grandchildren.Add(Url($"{name}-{i}-{j}-{k}", $"Topic {i}.{j}.{k}", $"https://docs.example/{i}/{j}/{k}"));
                }

                var child = Url($"{name}-{i}-{j}", $"Area {i}.{j}", $"https://docs.example/{i}/{j}");
                child["children"] = grandchildren;
                children.Add(child);
            }

            entries.Add(new JsonObject
            {
                ["id"] = $"{name}-{i}",
                ["target"] = new JsonObject { ["type"] = "page", ["pageId"] = targets[i].ToString() },
                ["children"] = children,
            });
        }

        var document = new JsonObject { ["schemaVersion"] = 1, ["entries"] = entries }.ToJsonString();
        using var body = new StringContent(
            $$"""{"version":{{menu.GetProperty("draftVersion").GetUInt32()}},"document":{{document}}}""", System.Text.Encoding.UTF8, "application/json");
        using var saved = await client.PutAsync(new Uri($"{root}/menus/{id}/draft", UriKind.Relative), body).ConfigureAwait(false);
        var reply = await saved.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, $"Saving the menu failed: {reply}");

        await PublishAsync(client, $"{root}/menus/{id}", JsonDocument.Parse(reply).RootElement.GetProperty("draftVersion").GetUInt32()).ConfigureAwait(false);
        return id;
    }

    private static JsonObject Url(string id, string label, string href) => new()
    {
        ["id"] = id,
        ["label"] = label,
        ["target"] = new JsonObject { ["type"] = "url", ["href"] = href },
        ["children"] = new JsonArray(),
    };

    private static string Heading(string text) =>
        $$"""{ "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [ { "id": "s", "type": "section", "props": {}, "children": [ { "id": "h", "type": "heading", "props": { "text": "{{text}}", "level": 1 }, "children": [] } ] } ] } }""";

    private static async Task<Guid> CreatePageAsync(HttpClient client, string root, string title, string slug)
    {
        using var created = await client.PostAsJsonAsync(new Uri($"{root}/pages", UriKind.Relative), new { title, slug }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
    }

    private static async Task<string> VisitAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/performance", UriKind.Relative));
        request.Headers.Host = Host;
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
        return JsonDocument.Parse(reply).RootElement.GetProperty("version").GetUInt32();
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
                new { setupCode, displayName = "Menu Perf Operator", email = "menu-perf@smoke.test", password = "menu performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
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
