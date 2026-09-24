using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// Sign-in stays fast under a burst, and does not starve the rest of the admin
/// (002 SC-013, research R-19).
/// </summary>
/// <remarks>
/// Runs against the built image with <c>docker-compose.perf.yml</c> limiting the app to the
/// reference environment's 2 CPUs and 4 GB. Only a real CPU limit makes the numbers mean
/// anything: password hashing is deliberately expensive, and in process it would share
/// every core the test host has.
/// <para>
/// Run on its own with <c>dotnet test tests/QuickBuilder.SmokeTests --filter
/// "Category=Performance"</c> (quickstart §11).
/// </para>
/// </remarks>
[Trait("Category", "Performance")]
[Collection(DeploymentGroup.Name)]
public sealed class SignInPerformanceTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const int Concurrency = 10;
    private const int Rounds = 20;
    private const string Email = "perf@smoke.test";
    private const string Password = "performance passphrase 2026";

    private static readonly TimeSpan SignInTarget = TimeSpan.FromSeconds(1);

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
    public async Task Sign_in_meets_its_target_under_a_concurrent_burst()
    {
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        // Cookies are sent by hand so that many concurrent requests share one connection
        // pool without sharing a cookie jar.
        using var handler = new SocketsHttpHandler { UseCookies = false, MaxConnectionsPerServer = Concurrency * 2 };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(ComposeDeployment.BaseAddress) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(true);
        var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Perf Operator", email = Email, password = Password })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        var session = SessionCookie(setup);

        // Warm up: the first requests pay for JIT and connection setup, which is not what
        // SC-013 measures.
        for (var i = 0; i < 3; i++)
        {
            await SignInAsync(client).ConfigureAwait(true);
            await ListSitesAsync(client, session).ConfigureAwait(true);
        }

        var idle = new List<TimeSpan>();
        for (var i = 0; i < 40; i++)
        {
            idle.Add(await ListSitesAsync(client, session).ConfigureAwait(true));
        }

        // The burst: 10 sign-ins at once, 20 times over, while one admin user keeps
        // browsing. The browsing latency is what SC-013's "does not degrade other admin
        // requests" is measured on.
        var signIns = new List<TimeSpan>();
        var duringBurst = new List<TimeSpan>();
        using var burstOver = new CancellationTokenSource();

        var browsing = Task.Run(async () =>
        {
            while (!burstOver.IsCancellationRequested)
            {
                duringBurst.Add(await ListSitesAsync(client, session).ConfigureAwait(false));
            }
        });

        for (var round = 0; round < Rounds; round++)
        {
            signIns.AddRange(await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => SignInAsync(client))).ConfigureAwait(true));
        }

        await burstOver.CancelAsync().ConfigureAwait(true);
        await browsing.ConfigureAwait(true);

        var signInP95 = P95(signIns);
        var idleP95 = P95(idle);
        var burstP95 = P95(duringBurst);

        output.WriteLine($"sign-in: n={signIns.Count} p50={Ms(P50(signIns))} p95={Ms(signInP95)} max={Ms(signIns.Max())}");
        output.WriteLine($"GET /api/v1/sites idle: n={idle.Count} p95={Ms(idleP95)}");
        output.WriteLine($"GET /api/v1/sites during burst: n={duringBurst.Count} p95={Ms(burstP95)}");

        var logs = await _deployment.ComposeAsync("logs --no-log-prefix app").ConfigureAwait(true);
        var slowHashing = logs.Output.Split('\n').FirstOrDefault(l => l.Contains("Password hashing took", StringComparison.Ordinal));
        if (slowHashing is not null)
        {
            output.WriteLine($"This host is slower than the reference environment: {slowHashing.Trim()}");
        }

        Assert.True(signInP95 <= SignInTarget, $"Sign-in p95 was {Ms(signInP95)}, above the {Ms(SignInTarget)} target.");
        Assert.True(
            burstP95 <= idleP95 * 2,
            $"Other admin requests slowed from p95 {Ms(idleP95)} idle to {Ms(burstP95)} during the burst (limit 2×).");
    }

    private static async Task<TimeSpan> SignInAsync(HttpClient client)
    {
        var watch = Stopwatch.StartNew();
        var response = await client
            .PostAsJsonAsync(new Uri("/api/v1/identity/sign-in", UriKind.Relative), new { email = Email, password = Password })
            .ConfigureAwait(false);
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return watch.Elapsed;
    }

    private static async Task<TimeSpan> ListSitesAsync(HttpClient client, string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/sites", UriKind.Relative));
        request.Headers.Add("Cookie", session);

        var watch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return watch.Elapsed;
    }

    private static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0];

    private static TimeSpan P50(IEnumerable<TimeSpan> samples) => Percentile(samples, 0.50);

    private static TimeSpan P95(IEnumerable<TimeSpan> samples) => Percentile(samples, 0.95);

    private static TimeSpan Percentile(IEnumerable<TimeSpan> samples, double percentile)
    {
        var sorted = samples.Order().ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private static string Ms(TimeSpan value) => $"{value.TotalMilliseconds:F0} ms";
}
