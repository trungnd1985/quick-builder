using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 018 SC-009: a page with two forms renders within 012's budgets: under 200 ms at p95 when it is
/// not cached, under 20 ms at p95 when it is.
/// </summary>
/// <remarks>
/// Measured as <see cref="MenuRenderPerformanceTests"/> measures. One form has every field type,
/// the other is short, so the page carries both, the form script and its hash in the policy. Each
/// miss follows a republish of the page, which gives it a new cache key.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class FormRenderPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "form-render.perf.test";
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
    public async Task A_page_with_two_forms_renders_within_budget_and_is_served_from_the_cache_faster_still()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await PerfApi.ClaimAsync(client, _deployment, "form-render-perf@smoke.test").ConfigureAwait(true);

        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Form render perf", hostNames = new[] { Host } })
            .ConfigureAwait(true);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var pages = $"/api/v1/pages/sites/{siteId}/pages";
        using var created = await client.PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = "Contact", slug = "contact" }).ConfigureAwait(true);
        var page = $"{pages}/{(await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid()}";

        const string Document = """
            {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s","type":"section","props":{},"children":[
              {"id":"enquiry","type":"form","props":{"name":"Enquiry","fields":[
                {"id":"a00000000001","type":"text","label":"Name","required":true,"maxLength":100},
                {"id":"a00000000002","type":"email","label":"Email","required":true,"help":"We reply to this address."},
                {"id":"a00000000003","type":"phone","label":"Phone"},
                {"id":"a00000000004","type":"textarea","label":"Message","required":true,"maxLength":2000},
                {"id":"a00000000005","type":"number","label":"Guests","min":1,"max":12,"wholeNumber":true},
                {"id":"a00000000006","type":"date","label":"Date"},
                {"id":"a00000000007","type":"select","label":"Topic","options":[{"value":"sales","label":"Sales"},{"value":"support","label":"Support"}]},
                {"id":"a00000000008","type":"radio","label":"Contact by","options":[{"value":"email","label":"Email"},{"value":"phone","label":"Phone"}]},
                {"id":"a00000000009","type":"checkboxes","label":"Interests","options":[{"value":"web","label":"Web"},{"value":"seo","label":"Search"}]},
                {"id":"a00000000010","type":"checkbox","label":"Send me the newsletter"}]},"children":[]},
              {"id":"newsletter","type":"form","props":{"name":"Newsletter","fields":[{"id":"b00000000001","type":"email","label":"Email","required":true}]},"children":[]}]}]}}
            """;
        var draftVersion = await PerfApi.SaveAsync(client, page, Document).ConfigureAwait(true);
        await PerfApi.PublishAsync(client, page, draftVersion).ConfigureAwait(true);

        var first = await VisitAsync(client).ConfigureAwait(true);
        Assert.Contains("data-qb-form=\"enquiry\"", first, StringComparison.Ordinal);
        Assert.Contains("data-qb-form=\"newsletter\"", first, StringComparison.Ordinal);

        var misses = new List<TimeSpan>();
        for (var run = 0; run < Misses; run++)
        {
            await PerfApi.PublishAsync(client, page, draftVersion).ConfigureAwait(true);
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
            $"Two forms, {first.Length} bytes: miss p50 {PerfApi.Ms(PerfApi.P(misses, 0.5))} p95 {PerfApi.Ms(PerfApi.P(misses, 0.95))}; " +
            $"hit p50 {PerfApi.Ms(PerfApi.P(hits, 0.5))} p95 {PerfApi.Ms(PerfApi.P(hits, 0.95))}");

        Assert.True(PerfApi.P(misses, 0.95) <= MissBudget, $"SC-009: rendering took {PerfApi.Ms(PerfApi.P(misses, 0.95))} at p95.");
        Assert.True(PerfApi.P(hits, 0.95) <= HitBudget, $"SC-009: a cached page took {PerfApi.Ms(PerfApi.P(hits, 0.95))} at p95.");
    }

    private static async Task<string> VisitAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/contact", UriKind.Relative));
        request.Headers.Host = Host;
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
}
