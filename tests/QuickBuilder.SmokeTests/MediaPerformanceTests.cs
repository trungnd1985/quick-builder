using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// 003 SC-008, SC-009 and SC-009a on the reference environment (2 CPUs, 4 GB), through the
/// built image and <c>docker-compose.perf.yml</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the load comes from.</b> The upload and serving measurements move hundreds of
/// megabytes. Sent from the test host, that traffic crosses the host's published-port
/// forwarder — on Docker Desktop, a user-space proxy — and the proxy, not the application,
/// dominated the numbers (quickstart-results §9: first byte p95 273 ms from the host, 53 ms
/// from inside the network). So those two measurements run from a <c>curl</c> container on
/// the compose network, reaching the app as a reverse proxy would. Setup and the paging
/// measurement, which move little data, run from the host.
/// </para>
/// <para>
/// One test, because the measurements share an expensive container lifecycle. Each reports
/// before anything is asserted, so one miss never hides the others.
/// </para>
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class MediaPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Host = "perf-media.test";
    private const string Email = "media-perf@smoke.test";
    private const string Password = "media performance passphrase 2026";
    private const string ClientImage = "curlimages/curl:8.16.0";

    private static readonly TimeSpan UploadTarget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FirstByteTarget = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PageTarget = TimeSpan.FromSeconds(1);

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
    public async Task Uploads_serving_and_paging_meet_their_targets()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress), Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        var session = await ClaimAsync(client).ConfigureAwait(true);
        var siteId = await CreateSiteAsync(client, session).ConfigureAwait(true);
        var network = await NetworkAsync().ConfigureAwait(true);

        var misses = new List<string>();
        misses.AddRange(await MeasureUploadsAsync(network, session, siteId).ConfigureAwait(true));
        misses.AddRange(await MeasureServingAsync(client, network, session, siteId).ConfigureAwait(true));
        misses.AddRange(await MeasurePagingAsync(client, session, siteId).ConfigureAwait(true));

        Assert.True(misses.Count == 0, string.Join("\n", misses));
    }

    /// <summary>SC-008: a 10 MB upload in ≤ 5 s, and other admin requests within 2× during 10 × 20 MiB uploads.</summary>
    private async Task<List<string>> MeasureUploadsAsync(string network, string session, Guid siteId)
    {
        var results = await InNetworkAsync(network, $$"""
            api=http://app:8080
            png() { printf '\211PNG\r\n\032\n' > "$2"; head -c $(($1 - 8)) /dev/zero >> "$2"; }
            upload() { curl -s -o /dev/null -w '%{http_code} %{time_total}\n' -H 'Cookie: {{session}}' -H 'X-QuickBuilder-Request: 1' -H 'Content-Type: image/png' -H 'X-QuickBuilder-File-Name: perf.png' --data-binary @"$1" "$api/api/v1/media/sites/{{siteId}}/files"; }
            list() { curl -s -o /dev/null -w '%{time_total}\n' -H 'Cookie: {{session}}' "$api/api/v1/sites"; }
            png 1024 /tmp/small.png; png 10000000 /tmp/ten.png; png 20971520 /tmp/big.png
            upload /tmp/small.png > /dev/null
            echo "single $(upload /tmp/ten.png)"
            for i in $(seq 5); do list > /dev/null; done
            for i in $(seq 40); do echo "idle $(list)"; done
            ( while [ ! -f /tmp/done ]; do echo "busy $(list)"; done ) &
            seq 10 | xargs -P 10 -I{} sh -c 'curl -s -o /dev/null -w "bulk %{http_code}\n" -H "Cookie: {{session}}" -H "X-QuickBuilder-Request: 1" -H "Content-Type: image/png" -H "X-QuickBuilder-File-Name: big.png" --data-binary @/tmp/big.png http://app:8080/api/v1/media/sites/{{siteId}}/files'
            touch /tmp/done; wait
            """).ConfigureAwait(true);

        var single = results.Single(r => r[0] == "single");
        Assert.True(single[1] == "201", $"The 10 MB upload answered {single[1]}.");
        Assert.All(results.Where(r => r[0] == "bulk"), r => Assert.Equal("201", r[1]));

        var singleTime = Seconds(single[2]);
        var idle = Times(results, "idle");
        var busy = Times(results, "busy");

        output.WriteLine($"10 MB upload: {Ms(singleTime)}");
        output.WriteLine($"GET /api/v1/sites idle p95 {Ms(P95(idle))}, during 10 x 20 MiB uploads p95 {Ms(P95(busy))} (n={busy.Count})");

        var misses = new List<string>();
        if (singleTime > UploadTarget)
        {
            misses.Add($"SC-008: a 10 MB upload took {Ms(singleTime)}, above {Ms(UploadTarget)}.");
        }

        if (P95(busy) > P95(idle) * 2)
        {
            misses.Add($"SC-008: other admin requests went from p95 {Ms(P95(idle))} idle to {Ms(P95(busy))} during the uploads (limit 2x).");
        }

        return misses;
    }

    /// <summary>SC-009: 50 concurrent requests for a 1 MB image, first byte within 100 ms at p95.</summary>
    private async Task<List<string>> MeasureServingAsync(HttpClient client, string network, string session, Guid siteId)
    {
        var publicPath = await UploadAsync(client, session, siteId, PngOfSize(1_000_000)).ConfigureAwait(true);

        var results = await InNetworkAsync(network, $$"""
            url=http://app:8080{{publicPath}}
            for i in $(seq 5); do curl -s -o /dev/null -H 'Host: {{Host}}' "$url"; done
            seq 200 | xargs -P 50 -I{} curl -s -o /dev/null -H 'Host: {{Host}}' -w 'ttfb %{http_code} %{time_starttransfer}\n' "$url"
            """).ConfigureAwait(true);

        var samples = results.Where(r => r[0] == "ttfb").ToList();
        Assert.All(samples, r => Assert.Equal("200", r[1]));
        var times = samples.Select(r => Seconds(r[2])).ToList();

        output.WriteLine($"1 MB image, 50 at once: first byte p50 {Ms(Percentile(times, 0.5))} p95 {Ms(P95(times))} (n={times.Count})");
        return P95(times) <= FirstByteTarget
            ? []
            : [$"SC-009: first-byte p95 was {Ms(P95(times))}, above {Ms(FirstByteTarget)}."];
    }

    /// <summary>
    /// SC-009a: the first, a middle and the last page of a 10,000-file library, each within
    /// 1 s through the API. Rows are seeded straight into the database; their bytes are not,
    /// so each listed file's availability check is a miss, which costs the same file-system
    /// lookup as a hit.
    /// </summary>
    private async Task<List<string>> MeasurePagingAsync(HttpClient client, string session, Guid siteId)
    {
        var seed = await _deployment.ComposeAsync(
            "exec -T db psql -U quickbuilder -d quickbuilder -v ON_ERROR_STOP=1 -c \"" +
            "INSERT INTO media.media_files (id, site_id, file_name, content_type, extension, size_bytes, uploaded_at_utc, uploaded_by, storage_key) " +
            "SELECT g.id, s.site, 'seed.png', 'image/png', 'png', 1024, now() - (g.n || ' seconds')::interval, gen_random_uuid(), " +
            "replace(s.site::text, '-', '') || '/media/' || replace(g.id::text, '-', '') " +
            // gen_random_uuid() in the select list runs per row; in an uncorrelated LATERAL it
            // would run once and every row would share an id.
            "FROM (SELECT gen_random_uuid() AS id, n FROM generate_series(1, 10000) AS n) g, " +
            $"(SELECT '{siteId}'::uuid AS site) s;\"").ConfigureAwait(true);
        Assert.True(seed.ExitCode == 0, $"Seeding 10,000 rows failed:\n{seed.Output}");

        var cursors = new List<string?> { null };
        string? cursor = null;
        do
        {
            cursor = (await ListMediaAsync(client, session, siteId, cursor).ConfigureAwait(true)).Cursor;
            if (cursor is not null)
            {
                cursors.Add(cursor);
            }
        }
        while (cursor is not null);

        var first = (await ListMediaAsync(client, session, siteId, cursors[0]).ConfigureAwait(true)).Elapsed;
        var middle = (await ListMediaAsync(client, session, siteId, cursors[cursors.Count / 2]).ConfigureAwait(true)).Elapsed;
        var last = (await ListMediaAsync(client, session, siteId, cursors[^1]).ConfigureAwait(true)).Elapsed;

        output.WriteLine($"library of {cursors.Count} pages: first {Ms(first)}, middle {Ms(middle)}, last {Ms(last)}");
        return first <= PageTarget && middle <= PageTarget && last <= PageTarget
            ? []
            : [$"SC-009a: a library page took longer than {Ms(PageTarget)}."];
    }

    /// <summary>The compose network the app is on.</summary>
    private async Task<string> NetworkAsync()
    {
        var container = (await _deployment.ComposeAsync("ps -q app").ConfigureAwait(false)).Output.Trim();
        var network = await _deployment.RunAsync(
            "docker",
            $"inspect -f \"{{{{range $k, $v := .NetworkSettings.Networks}}}}{{{{$k}}}}{{{{end}}}}\" {container}").ConfigureAwait(false);
        Assert.True(network.ExitCode == 0, network.Output);
        return network.Output.Trim();
    }

    /// <summary>
    /// Runs a shell script in a curl container on the compose network and returns its output
    /// as whitespace-split lines.
    /// </summary>
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

    /// <summary>Completes setup with the code from the logs and returns the session cookie.</summary>
    private async Task<string> ClaimAsync(HttpClient client)
    {
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(false);
        using var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Media Perf Operator", email = Email, password = Password })
            .ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        return setup.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static async Task<Guid> CreateSiteAsync(HttpClient client, string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/sites", UriKind.Relative))
        {
            Content = JsonContent.Create(new { name = "Perf media", hostNames = new[] { Host } }),
        };
        request.Headers.Add("Cookie", session);
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetGuid();
    }

    private static async Task<string> UploadAsync(HttpClient client, string session, Guid siteId, byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/media/sites/{siteId}/files", UriKind.Relative))
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        request.Headers.Add("X-QuickBuilder-File-Name", "perf.png");
        request.Headers.Add("Cookie", session);

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("publicPath").GetString()!;
    }

    private static async Task<(TimeSpan Elapsed, string? Cursor)> ListMediaAsync(HttpClient client, string session, Guid siteId, string? cursor)
    {
        var path = $"/api/v1/media/sites/{siteId}/files?limit=100" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", session);

        var watch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var next = page.GetProperty("nextCursor");
        return (watch.Elapsed, next.ValueKind == JsonValueKind.Null ? null : next.GetString());
    }

    /// <summary>A valid PNG signature padded to <paramref name="size"/> bytes.</summary>
    private static byte[] PngOfSize(int size)
    {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return bytes;
    }

    private static List<TimeSpan> Times(IEnumerable<string[]> results, string label) =>
        [.. results.Where(r => r[0] == label).Select(r => Seconds(r[1]))];

    private static TimeSpan Seconds(string value) =>
        TimeSpan.FromSeconds(double.Parse(value, CultureInfo.InvariantCulture));

    private static TimeSpan P95(IEnumerable<TimeSpan> samples) => Percentile(samples, 0.95);

    private static TimeSpan Percentile(IEnumerable<TimeSpan> samples, double percentile)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds:F0} ms";
}
