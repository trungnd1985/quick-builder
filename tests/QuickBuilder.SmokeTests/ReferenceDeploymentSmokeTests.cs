using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests;

/// <summary>
/// Boots the built image with the reference compose file and walks the operator journey
/// (Constitution Principle X, plan exception E-3).
/// </summary>
/// <remarks>
/// Everything else in the suite tests the application. This tests the <em>deployment</em> —
/// the Dockerfile, the compose file, the loopback binding, the health probes and the
/// startup ordering. Those are what an operator actually meets, and none of them is
/// exercised by <c>WebApplicationFactory</c>, which bypasses the image entirely.
/// <para>
/// The constitution specifies this smoke test as setup wizard → create page → publish →
/// view. Feature 002 added the first step: the journey now begins with the setup wizard,
/// reading the setup code from the container logs as an operator would, and runs every
/// later step as the administrator it created. Create page, publish and view arrive with
/// the Pages and Rendering specs (issue #4).
/// </para>
/// <para>
/// Excluded from the normal test run — CI filters on <c>FullyQualifiedName!~SmokeTests</c>
/// — because it builds an image and starts containers, which takes minutes rather than
/// seconds. It skips rather than fails when Docker is unavailable, so a developer without
/// a running daemon is not blocked.
/// </para>
/// </remarks>
[Trait("Category", "Smoke")]
public sealed class ReferenceDeploymentSmokeTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string BaseAddress = "http://127.0.0.1:8080";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    private readonly Stopwatch _sinceComposeUp = new();
    private string _repositoryRoot = string.Empty;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        _repositoryRoot = FindRepositoryRoot();
        _dockerAvailable = (await RunAsync("docker", "info").ConfigureAwait(false)).ExitCode == 0;

        if (!_dockerAvailable)
        {
            output.WriteLine("Docker is not available; smoke test will skip.");
            return;
        }

        // Start from nothing, including the volume: SC-001 is a claim about a *fresh*
        // installation, and a leftover database would let a broken migration path pass.
        await RunAsync("docker", "compose down -v").ConfigureAwait(false);

        var build = await RunAsync("docker", "compose build").ConfigureAwait(false);
        Assert.True(build.ExitCode == 0, $"docker compose build failed:\n{build.Output}");

        _sinceComposeUp.Start();
        var up = await RunAsync("docker", "compose up -d").ConfigureAwait(false);
        Assert.True(up.ExitCode == 0, $"docker compose up failed:\n{up.Output}");
    }

    public async Task DisposeAsync()
    {
        if (_dockerAvailable)
        {
            await RunAsync("docker", "compose down -v").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The whole operator journey against the real deployment.
    /// </summary>
    /// <remarks>
    /// Written as one test rather than several because the steps share an expensive
    /// container lifecycle and are meaningful only in sequence — a site must exist before
    /// it can be resolved. Each step reports through <c>ITestOutputHelper</c>, so a
    /// failure says which one broke.
    /// </remarks>
    [SkippableFact]
    public async Task Operator_journey_works_against_the_reference_deployment()
    {
        Skip.IfNot(_dockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };

        // 1. Readiness — the probe that means "migrations are done and the database is up".
        var readyIn = await WaitForReadyAsync(client).ConfigureAwait(true);
        output.WriteLine($"ready after {readyIn.TotalSeconds:F1}s");

        // 2. Liveness answers independently of readiness.
        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        // 3. A fresh installation is in setup mode: nothing administrative answers until it
        //    is claimed, and there are no default credentials to try (002 FR-001, FR-005).
        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.True(status.GetProperty("setupRequired").GetBoolean());
        var gated = await client.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal((HttpStatusCode)428, gated.StatusCode);
        output.WriteLine("fresh install: setup required");

        // 4. The admin shell is served from the image, not from a separate process.
        var admin = await client.GetAsync(new Uri("/admin/", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        output.WriteLine("admin shell served");

        // 5. The setup wizard, as an operator does it: the code comes from the logs.
        var setupCode = await ReadSetupCodeFromLogsAsync().ConfigureAwait(true);
        var setup = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/setup", UriKind.Relative),
                new { setupCode, displayName = "Smoke Operator", email = "operator@smoke.test", password = "smoke test passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        var session = setup.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal))
            .Split(';')[0];
        client.DefaultRequestHeaders.Add("Cookie", session);
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        // SC-001 evidence, recorded rather than asserted: CI runners vary too much for a
        // hard limit, and the quickstart run measures it on the reference environment.
        output.WriteLine($"setup complete; signed in {_sinceComposeUp.Elapsed.TotalSeconds:F1}s after compose up");

        // 6. As the new administrator: a fresh installation has no sites (FR-014a).
        var empty = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(0, empty.GetArrayLength());

        // 7. Create a site.
        var created = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Smoke", hostNames = new[] { "smoke.test" } })
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // 8. It appears in the list.
        var listed = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(1, listed.GetArrayLength());
        output.WriteLine("site created and listed");

        // 9. The public surface resolves it by host name — the end-to-end proof that site
        //    resolution works behind the real container, not just in-process. Visitors never
        //    authenticate, so this goes without the session.
        using var publicClient = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Host = "smoke.test";

        var resolved = await publicClient.SendAsync(request).ConfigureAwait(true);
        var payload = await resolved.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal("Smoke", payload.GetProperty("siteName").GetString());
        output.WriteLine("public host resolved to the created site");
    }

    /// <summary>
    /// FR-036: the container runs as a non-root user.
    /// </summary>
    /// <remarks>
    /// Cheap to assert and easy to regress — a base-image bump or an added `USER root`
    /// for a build step would silently undo it, and nothing else would notice.
    /// </remarks>
    [SkippableFact]
    public async Task Application_container_runs_as_a_non_root_user()
    {
        Skip.IfNot(_dockerAvailable, "Docker is not available on this machine.");

        var id = await RunAsync("docker", "compose exec -T app id -u").ConfigureAwait(true);

        Assert.Equal(0, id.ExitCode);
        Assert.NotEqual("0", id.Output.Trim());
        output.WriteLine($"container uid: {id.Output.Trim()}");
    }

    /// <summary>
    /// Finds the setup code in the container's log stream, as an operator would with
    /// <c>docker compose logs app</c> (002 research R-09). Logs are compact JSON, one event
    /// per line; the code is the <c>SetupCode</c> property of the one event that has it.
    /// </summary>
    private async Task<string> ReadSetupCodeFromLogsAsync()
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var logs = await RunAsync("docker", "compose logs --no-log-prefix app").ConfigureAwait(true);
            var line = logs.Output
                .Split('\n')
                .FirstOrDefault(l => l.Contains("\"SetupCode\"", StringComparison.Ordinal));

            if (line is not null)
            {
                using var document = JsonDocument.Parse(line.Trim());
                return document.RootElement.GetProperty("SetupCode").GetString()!;
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        }

        throw new InvalidOperationException("The setup code never appeared in the application's logs.");
    }

    private async Task<TimeSpan> WaitForReadyAsync(HttpClient client)
    {
        var started = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - started < ReadyTimeout)
        {
            try
            {
                var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return DateTimeOffset.UtcNow - started;
                }
            }
            catch (HttpRequestException)
            {
                // The container is still starting; the port is not listening yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        }

        var logs = await RunAsync("docker", "compose logs --tail 40 app").ConfigureAwait(true);
        throw new InvalidOperationException(
            $"The application never reported ready within {ReadyTimeout}. Recent logs:\n{logs.Output}");
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(true);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(true);

        await process.WaitForExitAsync().ConfigureAwait(true);

        return (process.ExitCode, stdout + stderr);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
