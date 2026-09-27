using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 010 SC-004 and SC-005, research P-20: on a site with 2,000 pages, the whole tree is listed
/// in under 2 s at p95, and moving or renaming a page with 200 descendants — every address
/// recomputed — completes in under 2 s at p95.
/// </summary>
/// <remarks>
/// Measured against the built image on the reference compose setup, from the test process,
/// so the figures include the HTTP round trip an admin shell would pay. The site is seeded
/// through the API: 200 pages under one branch (the subtree that moves), and the rest spread
/// under twenty sections, so no sibling group is unrealistically long.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class PageTreePerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int Pages = 2000;
    private const int Descendants = 200;
    private const int Sections = 20;
    private const int Runs = 20;

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

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
    public async Task A_full_tree_lists_and_a_200_page_subtree_moves_and_renames_within_two_seconds()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        using var site = await client
            .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = "Pages perf", hostNames = new[] { "pages.perf.test" } })
            .ConfigureAwait(true);
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("id").GetGuid();
        var pages = $"/api/v1/pages/sites/{siteId}/pages";

        var seeding = Stopwatch.StartNew();
        var branch = await CreateAsync(client, pages, "branch", null).ConfigureAwait(true);
        var target = await CreateAsync(client, pages, "target", null).ConfigureAwait(true);
        for (var i = 0; i < Descendants; i++)
        {
            await CreateAsync(client, pages, $"leaf-{i}", branch).ConfigureAwait(true);
        }

        var sections = new List<Guid>();
        for (var s = 0; s < Sections; s++)
        {
            sections.Add(await CreateAsync(client, pages, $"section-{s}", null).ConfigureAwait(true));
        }

        for (var i = Descendants + Sections + 2; i < Pages; i++)
        {
            await CreateAsync(client, pages, $"page-{i}", sections[i % Sections]).ConfigureAwait(true);
        }

        output.WriteLine($"{Pages} pages seeded in {seeding.Elapsed.TotalSeconds:F0}s");

        var listing = new List<TimeSpan>();
        for (var run = 0; run < Runs; run++)
        {
            var timer = Stopwatch.StartNew();
            using var response = await client.GetAsync(new Uri(pages, UriKind.Relative)).ConfigureAwait(true);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
            listing.Add(timer.Elapsed);
            Assert.Equal(Pages, body.GetProperty("pages").GetArrayLength());
        }

        var moving = new List<TimeSpan>();
        var renaming = new List<TimeSpan>();
        var version = 1;
        for (var run = 0; run < Runs; run++)
        {
            // Back and forth: under the target page, then back to the top level.
            var parent = run % 2 == 0 ? target : (Guid?)null;
            var timer = Stopwatch.StartNew();
            using var moved = await client
                .PostAsJsonAsync(new Uri($"{pages}/{branch}/move", UriKind.Relative), new { parentId = parent, afterPageId = (Guid?)null, version })
                .ConfigureAwait(true);
            moving.Add(timer.Elapsed);
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            version++;

            timer.Restart();
            using var renamed = await client
                .PutAsJsonAsync(new Uri($"{pages}/{branch}", UriKind.Relative), new { title = "Branch", slug = $"branch-{run}", version })
                .ConfigureAwait(true);
            renaming.Add(timer.Elapsed);
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            version++;
        }

        output.WriteLine($"list {Pages} pages p95 {Ms(P95(listing))}; move {Descendants + 1} pages p95 {Ms(P95(moving))}; rename p95 {Ms(P95(renaming))}");

        Assert.True(P95(listing) <= Budget, $"SC-005: listing took {Ms(P95(listing))} at p95.");
        Assert.True(P95(moving) <= Budget, $"SC-004: moving took {Ms(P95(moving))} at p95.");
        Assert.True(P95(renaming) <= Budget, $"SC-004: renaming took {Ms(P95(renaming))} at p95.");
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
                new { setupCode, displayName = "Pages Perf Operator", email = "pages-perf@smoke.test", password = "pages performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private static TimeSpan P95(IEnumerable<TimeSpan> samples)
    {
        var sorted = samples.Order().ToList();
        return sorted[(int)Math.Ceiling(sorted.Count * 0.95) - 1];
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms";
}
