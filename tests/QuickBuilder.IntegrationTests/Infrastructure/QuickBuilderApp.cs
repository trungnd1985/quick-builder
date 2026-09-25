using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real application pipeline against a real database.
/// </summary>
/// <remarks>
/// Principle X: tests drive the pipeline that ships. Nothing here replaces middleware or
/// stubs a handler — the only things overridden are the connection string and whether
/// background services run, because a test needs to control when dispatch happens rather
/// than race a five-second timer.
/// <para>
/// One test-only middleware is added in front of the pipeline: it lets a request choose
/// the client address it appears to come from (<see cref="IdentitySeed.RemoteAddressHeader"/>),
/// which throttling and the HTTPS rule depend on and <c>TestServer</c> otherwise leaves
/// empty. Every client uses <c>https://localhost</c>, as a production browser behind TLS
/// would (002 research R-18).
/// </para>
/// </remarks>
public sealed class QuickBuilderApp(
    string connectionString,
    bool runBackgroundServices = false,
    Action<IServiceCollection>? configureTestServices = null,
    string? storagePath = null,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    // 003: each instance stores files in its own temporary directory unless the test
    // passes one — two instances sharing a directory is how the multi-instance tests model
    // one shared volume (SC-007).
    private readonly bool _ownsStorage = storagePath is null;

    /// <summary>The local storage directory this instance uses.</summary>
    public string StoragePath { get; } = storagePath ?? CreateTemporaryStorage();

    /// <summary>A fresh, empty directory for a storage root. The caller deletes it.</summary>
    public static string CreateTemporaryStorage()
    {
        var path = Path.Combine(Path.GetTempPath(), "quickbuilder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Storage:Local:Path", StoragePath);

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        // Outbox dispatch is driven explicitly in tests via OutboxDispatcher.RunOnceAsync,
        // so a test asserts on a pass it triggered rather than waiting out a poll interval
        // and hoping. Tests that want the timer can opt in.
        builder.UseSetting("Outbox:Enabled", runBackgroundServices ? "true" : "false");

        IdentitySeed.UseTestRemoteAddress(builder);

        builder.ConfigureServices(services => services.AddSingleton<Serilog.Core.ILogEventSink>(Logs));

        builder.ConfigureServices(services =>
        {
            if (!runBackgroundServices)
            {
                // Remove the hosted services so nothing dispatches or sweeps behind the
                // test's back. The dispatcher itself is still resolvable and is driven
                // directly by the outbox tests.
                var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
                foreach (var descriptor in hosted)
                {
                    services.Remove(descriptor);
                }
            }
        });

        // Last, so a test's replacement — a manual clock, a cheaper hasher — wins.
        if (configureTestServices is not null)
        {
            builder.ConfigureTestServices(configureTestServices);
        }
    }

    /// <summary>
    /// A client that sends the given Host header, for exercising site resolution.
    /// </summary>
    /// <remarks>
    /// The public surface resolves a site from this header (FR-011a), so most public-path
    /// tests need to control it. The default client sends <c>localhost</c>, which resolves
    /// to nothing — which is itself the FR-014 case.
    /// </remarks>
    /// <summary>
    /// Releases idle pooled connections along with the application. Npgsql pools outlive the
    /// host that opened them, and one per test database adds up to the server's connection
    /// limit over a full run. Safe here because the integration tests run one class at a
    /// time (see <see cref="DatabaseCollection"/>).
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Npgsql.NpgsqlConnection.ClearAllPools();

            if (_ownsStorage)
            {
                TryDeleteDirectory(StoragePath);
            }
        }
    }

    /// <summary>Removes a test storage directory, tolerating files still held open.</summary>
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is harmless; failing the run over one is not.
        }
    }

    /// <summary>Every log event this application instance has written.</summary>
    public LogCapture Logs { get; } = new();

    public HttpClient CreateClientForHost(string hostName) => IdentitySeed.CreateAnonymousClient(this, hostName);

    /// <summary>A client with no session, for the public surface and for refusal tests.</summary>
    public HttpClient CreateAnonymousClient() => IdentitySeed.CreateAnonymousClient(this);

    /// <summary>
    /// A signed-in Administrator on a claimed installation: the caller 001's tests assumed
    /// when the admin surface was open.
    /// </summary>
    public Task<HttpClient> CreateAdminClientAsync(string? hostName = null) =>
        IdentitySeed.CreateAdminClientAsync(this, hostName);
}
