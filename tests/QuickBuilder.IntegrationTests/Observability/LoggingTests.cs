using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// One identifier follows a request end to end, and nothing sensitive travels with it
/// (SC-008, FR-031, FR-031a, FR-032).
/// </summary>
/// <remarks>
/// SC-008 asks for two things that pull in opposite directions: enough context to trace
/// one request through the logs, and none of the content that makes logs a liability.
/// Both are tested here because satisfying either alone is easy.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class LoggingTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("logging").ConfigureAwait(true);
        _app = new QuickBuilderApp(connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// FR-031a: the identifier returned to a caller is the trace identifier.
    /// </summary>
    /// <remarks>
    /// This is the whole of SC-008's "trace one request by one identifier". An error
    /// response carries a <c>traceId</c>; the log entries for that request carry the same
    /// value because both read it from the current activity. If the response invented its
    /// own identifier, an operator holding it could find nothing.
    /// <para>
    /// Asserted by supplying a W3C <c>traceparent</c> and checking the response echoes
    /// that trace id back — which only holds if the correlation identifier really is the
    /// trace, not a parallel scheme.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Error_responses_carry_the_trace_id_of_the_request()
    {
        var traceId = ActivityTraceId.CreateRandom().ToString();
        var spanId = ActivitySpanId.CreateRandom().ToString();

        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{spanId}-01");

        // A validation failure is the cheapest way to get a problem-details body back.
        var response = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "", hostNames = Array.Empty<string>() })
            .ConfigureAwait(true);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.True(problem.TryGetProperty("traceId", out var returned));
        Assert.Equal(traceId, returned.GetString());
    }

    /// <summary>
    /// FR-032: a rejected request does not echo secrets back, and nothing logs the payload.
    /// </summary>
    /// <remarks>
    /// The realistic leak is not a deliberate log of a password — it is a validation error
    /// helpfully quoting the offending value, or an exception message carrying the request
    /// body. Both would put a secret somewhere it is retained and shipped to a log
    /// collector.
    /// <para>
    /// A site name is not itself sensitive; it stands in for any user-supplied value, and
    /// the assertion is that a marker string submitted in a request does not come back in
    /// the response body.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Rejected_requests_do_not_echo_the_submitted_value()
    {
        const string secretish = "hunter2-correct-horse-battery-staple";

        var client = _app.CreateClient();

        var response = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                // The name is fine; the host name is not, so this fails validation while
                // carrying the marker in a field that error messages might quote.
                new { name = secretish, hostNames = new[] { "not a valid host" } })
            .ConfigureAwait(true);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.DoesNotContain(secretish, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unexpected failure reveals nothing internal to the caller (FR-034).
    /// </summary>
    /// <remarks>
    /// Checks the negative: no stack frame, no type name, no connection string fragment.
    /// The caller gets a trace id and nothing else, which is the only detail that helps
    /// them and the only one safe to give.
    /// </remarks>
    [Fact]
    public async Task Unknown_routes_do_not_leak_internals()
    {
        var client = _app.CreateClient();

        var response = await client
            .GetAsync(new Uri("/api/v1/sites/not-a-guid", UriKind.Relative))
            .ConfigureAwait(true);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        foreach (var leak in new[] { "QuickBuilder.Sites.Infrastructure", "Npgsql", "Password=", "at QuickBuilder" })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
