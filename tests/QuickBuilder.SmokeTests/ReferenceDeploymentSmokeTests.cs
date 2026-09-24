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
[Collection(DeploymentGroup.Name)]
public sealed class ReferenceDeploymentSmokeTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string BaseAddress = ComposeDeployment.BaseAddress;

    private readonly ComposeDeployment _deployment = new();

    public async Task InitializeAsync()
    {
        if (!await _deployment.StartAsync().ConfigureAwait(false))
        {
            output.WriteLine("Docker is not available; smoke test will skip.");
        }
    }

    public Task DisposeAsync() => _deployment.StopAsync();

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
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        using var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };

        // What the admin shell sends on every state-changing call (002 research R-11).
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");

        // 1. Readiness — the probe that means "migrations are done and the database is up".
        var readyIn = await _deployment.WaitForReadyAsync(client).ConfigureAwait(true);
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
        var setupCode = await _deployment.ReadSetupCodeFromLogsAsync().ConfigureAwait(true);
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

        // SC-001 evidence, recorded rather than asserted: CI runners vary too much for a
        // hard limit, and the quickstart run measures it on the reference environment.
        output.WriteLine($"setup complete; signed in {_deployment.SinceUp.Elapsed.TotalSeconds:F1}s after compose up");

        // 5a. The admin surface refuses anyone without a session (002 FR-024, E-1 closed).
        using (var anonymous = new HttpClient { BaseAddress = new Uri(BaseAddress) })
        {
            var refused = await anonymous.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // 5b. Sign out, and the session is gone for good; sign in again with the same
        //     credentials (002 US2).
        var signedOut = await client.PostAsync(new Uri("/api/v1/identity/sign-out", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        var replayed = await client.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        var signIn = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/sign-in", UriKind.Relative),
                new { email = "operator@smoke.test", password = "smoke test passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add(
            "Cookie",
            signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("qb_session=", StringComparison.Ordinal)).Split(';')[0]);
        output.WriteLine("anonymous refused; signed out and back in");

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

        // 10. Operator recovery (002 US5): with command-line access to the container, the
        //     operator issues a set-password link for the administrator, sets a new password
        //     through it, and signs in with that password.
        var recovery = await _deployment.ComposeAsync(
            "exec -T app dotnet QuickBuilder.Host.dll identity recovery-link --email operator@smoke.test").ConfigureAwait(true);
        Assert.True(recovery.ExitCode == 0, $"identity recovery-link failed:\n{recovery.Output}");

        var token = Uri.UnescapeDataString(
            recovery.Output.Split("#token=")[1].Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)[0]);

        using var recovered = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        recovered.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        var reset = await recovered
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/set-password", UriKind.Relative),
                new { token, newPassword = "recovered smoke passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var signInAgain = await recovered
            .PostAsJsonAsync(
                new Uri("/api/v1/identity/sign-in", UriKind.Relative),
                new { email = "operator@smoke.test", password = "recovered smoke passphrase 2026" })
            .ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, signInAgain.StatusCode);
        output.WriteLine("recovery link issued from the command line; new password signs in");
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
        Skip.IfNot(_deployment.DockerAvailable, "Docker is not available on this machine.");

        var id = await _deployment.ComposeAsync("exec -T app id -u").ConfigureAwait(true);

        Assert.Equal(0, id.ExitCode);
        Assert.NotEqual("0", id.Output.Trim());
        output.WriteLine($"container uid: {id.Output.Trim()}");
    }
}
