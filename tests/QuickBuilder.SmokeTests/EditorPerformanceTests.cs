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
/// 011 SC-005 (server side) and research ED-16: on the reference setup, what the editor
/// fetches to open a 500-block page — its draft, the site's design tokens and its components —
/// arrives in under 1 s at p95; and counting the blocks that use a design token across a site
/// of 2,000 pages, each with a 20-block draft, takes under 1 s at p95.
/// </summary>
/// <remarks>
/// Measured against the built image on the reference compose setup, from the test process,
/// so the figures include the HTTP round trips the admin app pays. The editor's own
/// interaction timings (select, change, drop in under 100 ms) need a browser, and are measured
/// by hand from the quickstart until the harness in #25 (plan exception E-5).
/// One test, so the 2,000-page site is seeded once for both measurements.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class EditorPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int Pages = 2000;
    private const int Sections = 20;
    private const int Runs = 20;

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

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
    public async Task The_editor_opens_a_500_block_page_and_counts_token_use_within_a_second()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Editor perf", hostNames = new[] { "editor.perf.test" } })
            .ConfigureAwait(true);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var pages = $"/api/v1/pages/sites/{siteId}/pages";

        // The 500-block page the editor opens.
        var large = await CreateAsync(client, pages, "large", null).ConfigureAwait(true);
        var fixture = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "tests", "fixtures", "editor", "page-500.json")).ConfigureAwait(true);
        await SaveDraftAsync(client, $"{pages}/{large}/draft", fixture).ConfigureAwait(true);

        // The rest of a 2,000-page site, each page with a 20-block draft using the token.
        var seeding = Stopwatch.StartNew();
        var sections = new List<Guid>();
        for (var s = 0; s < Sections; s++)
        {
            sections.Add(await CreateAsync(client, pages, $"section-{s}", null).ConfigureAwait(true));
        }

        var draft = TwentyBlocks();
        for (var i = Sections + 1; i < Pages; i++)
        {
            var id = await CreateAsync(client, pages, $"page-{i}", sections[i % Sections]).ConfigureAwait(true);
            await SaveDraftAsync(client, $"{pages}/{id}/draft", draft).ConfigureAwait(true);
        }

        output.WriteLine($"{Pages} pages with drafts seeded in {seeding.Elapsed.TotalSeconds:F0}s");

        var opening = new List<TimeSpan>();
        for (var run = 0; run < Runs; run++)
        {
            var timer = Stopwatch.StartNew();
            var responses = await Task.WhenAll(
                client.GetAsync(new Uri($"{pages}/{large}/draft", UriKind.Relative)),
                client.GetAsync(new Uri($"/api/v1/editor/sites/{siteId}/design-tokens", UriKind.Relative)),
                client.GetAsync(new Uri($"/api/v1/pages/sites/{siteId}/components", UriKind.Relative))).ConfigureAwait(true);
            foreach (var response in responses)
            {
                await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
            }

            opening.Add(timer.Elapsed);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var counting = new List<TimeSpan>();
        var blockCount = 0;
        for (var run = 0; run < Runs; run++)
        {
            var timer = Stopwatch.StartNew();
            var usage = await client
                .GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/pages/sites/{siteId}/token-usages/color-brand", UriKind.Relative))
                .ConfigureAwait(true);
            counting.Add(timer.Elapsed);
            blockCount = usage.GetProperty("blockCount").GetInt32();
        }

        output.WriteLine($"open 500-block page p95 {Ms(P95(opening))}; count token use over {Pages} drafts ({blockCount} blocks) p95 {Ms(P95(counting))}");

        Assert.True(blockCount >= (Pages - Sections - 1) * 10, $"Expected every seeded draft counted, got {blockCount} blocks.");
        Assert.True(P95(opening) <= Budget, $"SC-005: opening took {Ms(P95(opening))} at p95.");
        Assert.True(P95(counting) <= Budget, $"ED-16: counting took {Ms(P95(counting))} at p95.");
    }

    /// <summary>A section holding 19 blocks, half of them using the brand colour.</summary>
    private static string TwentyBlocks()
    {
        var children = string.Join(',', Enumerable.Range(0, 19).Select(i => i % 2 == 0
            ? $$$"""{"id":"h{{{i}}}","type":"heading","props":{"text":"Heading {{{i}}}","color":{"token":"color-brand"}},"children":[]}"""
            : $$$"""{"id":"t{{{i}}}","type":"text","props":{"text":"Text {{{i}}}"},"children":[]}"""));
        return $$$"""{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s","type":"section","props":{"background":{"token":"color-brand"}},"children":[{{{children}}}]}]}}""";
    }

    private static async Task SaveDraftAsync(HttpClient client, string path, string document)
    {
        var current = await client.GetFromJsonAsync<JsonElement>(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        using var body = new StringContent(
            $$"""{"version":{{current.GetProperty("version").GetUInt32()}},"document":{{document}}}""",
            Encoding.UTF8,
            "application/json");
        using var saved = await client.PutAsync(new Uri(path, UriKind.Relative), body).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string pages, string slug, Guid? parentId)
    {
        using var created = await client
            .PostAsJsonAsync(new Uri(pages, UriKind.Relative), new { title = slug, slug, parentId })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
    }

    private async Task ClaimAsync(HttpClient client)
    {
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Editor Perf Operator", email = "editor-perf@smoke.test", password = "editor performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (QuickBuilder.slnx) was not found above the test binaries.");
    }

    private static TimeSpan P95(IEnumerable<TimeSpan> samples)
    {
        var sorted = samples.Order().ToList();
        return sorted[(int)Math.Ceiling(sorted.Count * 0.95) - 1];
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms";
}
