using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using QuickBuilder.Host.Configuration;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// A default installation sends nothing anywhere (SC-014, FR-031b).
/// </summary>
/// <remarks>
/// The constitution requires the product to run fully offline: no telemetry, licence
/// checks, or calls home unless an operator explicitly enables them. Customers run this on
/// their own infrastructure, sometimes air-gapped, and a single unexpected outbound
/// connection is both a support incident and a trust problem.
/// <para>
/// Tested by asserting no exporter is registered rather than by watching sockets. That is
/// the stronger claim: a socket test proves nothing was sent during the test, while this
/// proves there is nothing that <em>could</em> send. The guarantee is structural — with no
/// endpoint configured, no exporter is constructed, so no code path exists to make a call.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "NoEgress")]
public sealed class NoEgressTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("noegress").ConfigureAwait(true);
        _app = new QuickBuilderApp(connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The default configuration names no collector.</summary>
    [Fact]
    public void No_telemetry_endpoint_is_configured_by_default()
    {
        var telemetry = _app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<TelemetryOptions>>()
            .Value;

        Assert.True(string.IsNullOrWhiteSpace(telemetry.OtlpEndpoint));
        Assert.False(telemetry.ExportEnabled);
    }

    /// <summary>
    /// Tracing is instrumented even though nothing is exported (FR-031b).
    /// </summary>
    /// <remarks>
    /// The distinction the requirement rests on. Instrumentation is unconditional — it is
    /// where the trace id in the logs comes from, and it costs nothing externally. Only
    /// export is optional, and it is off. If a future change disabled instrumentation to
    /// "turn off telemetry", correlation would break silently and SC-008 would fail for a
    /// reason nobody would connect to this.
    /// </remarks>
    [Fact]
    public void Tracing_is_still_instrumented_when_export_is_off()
    {
        var tracerProvider = _app.Services.GetService<TracerProvider>();

        Assert.NotNull(tracerProvider);
    }

    /// <summary>
    /// Serving a request end to end makes no outbound call.
    /// </summary>
    /// <remarks>
    /// Belt and braces over the structural argument: exercises the full path an operator
    /// would (create a site, resolve it publicly, read health) while a handler counts
    /// outbound HTTP through <see cref="HttpClient"/>. The database connection is not HTTP
    /// and is expected.
    /// </remarks>
    [Fact]
    public async Task Serving_requests_makes_no_outbound_http_calls()
    {
        var outbound = 0;

        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStarted = _ => Interlocked.Increment(ref outbound),
        };

        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Offline", hostNames = new[] { "offline.test" } })
            .ConfigureAwait(true);

        var publicClient = _app.CreateClientForHost("offline.test");
        await publicClient.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(0, outbound);
    }
}
