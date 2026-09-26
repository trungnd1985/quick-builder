using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 009 SC-007, research S-20: with 100 sites and 500 host names, resolving the site adds no
/// more than 5 ms to the 95th-percentile public response time, measured one request at a
/// time. The same comparison under 20 concurrent requests is reported, not asserted.
/// </summary>
/// <remarks>
/// Resolution reads the database on every public request (research S-01), so this is the
/// check that the price of correctness across instances stays small. It compares two
/// requests the same app serves under the same load: <c>GET /</c> on a site's host, which
/// resolves a site, and <c>GET /health/live</c>, which does not. The difference is the cost
/// of resolution. Load comes from a curl container on the compose network, as for Media,
/// so the host machine's networking is not what gets measured.
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class SiteResolutionPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int Sites = 100;
    private const int HostNamesPerSite = 5;
    private const int Requests = 2000;
    private const int Concurrency = 20;
    private const string ClientImage = "curlimages/curl:8.16.0";

    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(5);

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
    public async Task Resolution_adds_at_most_five_milliseconds_at_p95()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        await ClaimAsync(client).ConfigureAwait(true);

        for (var site = 0; site < Sites; site++)
        {
            var hostNames = Enumerable.Range(0, HostNamesPerSite).Select(h => $"h{h}.s{site}.perf.test").ToArray();
            using var created = await client
                .PostAsJsonAsync(new Uri("/api/v1/sites", UriKind.Relative), new { name = $"Perf {site}", hostNames })
                .ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        output.WriteLine($"{Sites} sites, {Sites * HostNamesPerSite} host names");

        // Each resolving request uses a different host name, round-robin over all 500, so
        // no single name gets an advantage.
        var results = await InNetworkAsync(await NetworkAsync().ConfigureAwait(true), $$"""
            for i in $(seq 100); do curl -s -o /dev/null http://app:8080/health/live; curl -s -o /dev/null -H "Host: h0.s0.perf.test" http://app:8080/; done
            seq 0 {{Requests - 1}} | xargs -P {{Concurrency}} -I{} sh -c 'curl -s -o /dev/null -w "site %{http_code} %{time_total}\n" -H "Host: h$(( {} % {{HostNamesPerSite}} )).s$(( {} / {{HostNamesPerSite}} % {{Sites}} )).perf.test" http://app:8080/'
            seq {{Requests}} | xargs -P {{Concurrency}} -I{} sh -c 'curl -s -o /dev/null -w "health %{http_code} %{time_total}\n" http://app:8080/health/live'
            seq {{Requests}} | xargs -P {{Concurrency}} -I{} sh -c 'curl -s -o /dev/null -w "unknown %{http_code} %{time_total}\n" -H "Host: nobody{}.perf.test" http://app:8080/'
            for i in $(seq 300); do curl -s -o /dev/null -w "seqsite %{http_code} %{time_total}\n" -H "Host: h1.s$(( i % {{Sites}} )).perf.test" http://app:8080/; done
            for i in $(seq 300); do curl -s -o /dev/null -w "seqhealth %{http_code} %{time_total}\n" http://app:8080/health/live; done
            """).ConfigureAwait(true);

        var sites = results.Where(r => r[0] == "site").ToList();
        var health = results.Where(r => r[0] == "health").ToList();
        Assert.All(sites, r => Assert.Equal("200", r[1]));
        Assert.All(health, r => Assert.Equal("200", r[1]));

        var siteP95 = P95(sites.Select(r => Seconds(r[2])));
        var healthP95 = P95(health.Select(r => Seconds(r[2])));
        var unknownP95 = P95(results.Where(r => r[0] == "unknown").Select(r => Seconds(r[2])));
        output.WriteLine($"unknown host (resolution only, no controller) p95 {Ms(unknownP95)}");
        var sequentialSite = results.Where(r => r[0] == "seqsite").Select(r => Seconds(r[2])).ToList();
        var sequentialHealth = results.Where(r => r[0] == "seqhealth").Select(r => Seconds(r[2])).ToList();
        output.WriteLine($"one at a time: GET / p95 {Ms(P95(sequentialSite))}, /health/live p95 {Ms(P95(sequentialHealth))}");
        output.WriteLine($"GET / p95 {Ms(siteP95)} (n={sites.Count}); /health/live p95 {Ms(healthP95)} (n={health.Count}); difference {Ms(siteP95 - healthP95)}");

        // SC-007 is asserted one request at a time: what resolution itself adds. Under 20 at
        // once on two CPUs the same cost is amplified by queueing to about 10 ms; that figure
        // is reported above, not asserted, and is recorded as a risk for Rendering (plan.md).
        // Meeting it under load needs cache invalidation across instances (exception E-4).
        var added = P95(sequentialSite) - P95(sequentialHealth);
        Assert.True(
            added <= Budget,
            $"SC-007: resolution added {Ms(added)} at p95 one request at a time, above {Ms(Budget)}.");
    }

    private async Task ClaimAsync(HttpClient client)
    {
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Resolution Perf Operator", email = "resolution-perf@smoke.test", password = "resolution performance passphrase 2026" })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        client.DefaultRequestHeaders.Add(
            "Cookie",
            setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
    }

    private async Task<string> NetworkAsync()
    {
        var container = (await _deployment.ComposeAsync("ps -q app").ConfigureAwait(false)).Output.Trim();
        var network = await _deployment.RunAsync(
            "docker",
            $"inspect -f \"{{{{range $k, $v := .NetworkSettings.Networks}}}}{{{{$k}}}}{{{{end}}}}\" {container}").ConfigureAwait(false);
        Assert.True(network.ExitCode == 0, network.Output);
        return network.Output.Trim();
    }

    private static async Task<List<string[]>> InNetworkAsync(string network, string script)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("docker", $"run --rm -i --network {network} --entrypoint sh {ClientImage} -s")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        await process.StandardInput.WriteAsync(script.Replace("\r\n", "\n", StringComparison.Ordinal)).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);

        var text = await stdout.ConfigureAwait(false);
        Assert.True(process.ExitCode == 0, $"The in-network client failed:\n{text}\n{await stderr.ConfigureAwait(false)}");

        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))];
    }

    private static TimeSpan Seconds(string value) =>
        TimeSpan.FromSeconds(double.Parse(value, CultureInfo.InvariantCulture));

    private static TimeSpan P95(IEnumerable<TimeSpan> samples)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(0.95 * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds:F1} ms";
}
