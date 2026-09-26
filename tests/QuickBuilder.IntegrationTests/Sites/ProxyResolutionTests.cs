using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// 009 US3 scenarios 2 and 3, FR-016, SC-006, research S-14: forwarded host, scheme and
/// client address come from trusted proxies only — one hop or several — and an untrusted
/// hop can never choose which site a request reaches.
/// </summary>
/// <remarks>
/// The client address a request "arrives from" is set by the test-only header from
/// <see cref="IdentitySeed.RemoteAddressHeader"/>. What the forwarded-headers middleware
/// made of the request is captured after the pipeline ran, because it changes the request
/// in place.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ProxyResolutionTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Proxy = "10.0.0.1";
    private const string Cdn = "10.0.0.2";
    private const string Stranger = "198.51.100.7";

    private readonly RequestCapture _capture = new();
    private QuickBuilderApp _app = null!;
    private Guid _siteA;
    private Guid _siteB;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("proxy").ConfigureAwait(false),
            configureTestServices: services =>
            {
                services.AddSingleton(_capture);
                services.AddTransient<IStartupFilter, CaptureStartupFilter>();
            },
            settings: new Dictionary<string, string>
            {
                ["Hosting:KnownProxies:0"] = Proxy,
                ["Hosting:KnownProxies:1"] = Cdn,
            });

        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
        _siteA = (await SitesApi.CreateOkAsync(admin, "A", ["a.test"]).ConfigureAwait(false)).GetProperty("id").GetGuid();
        _siteB = (await SitesApi.CreateOkAsync(admin, "B", ["b.test"]).ConfigureAwait(false)).GetProperty("id").GetGuid();
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_trusted_proxy_forwards_the_visitors_host_scheme_and_address()
    {
        var resolved = await ResolveAsync(from: Proxy, host: "internal.local", forwardedHost: "B.test:443", forwardedFor: "203.0.113.9", forwardedProto: "http").ConfigureAwait(true);

        // The test client speaks HTTPS, so seeing "http" proves the forwarded value was used.
        Assert.Equal(_siteB, resolved);
        Assert.Equal("http", _capture.Scheme);
        Assert.Equal("203.0.113.9", _capture.RemoteAddress);
    }

    [Fact]
    public async Task Forwarded_headers_from_an_untrusted_address_are_ignored()
    {
        var resolved = await ResolveAsync(from: Stranger, host: "a.test", forwardedHost: "b.test", forwardedFor: "203.0.113.9", forwardedProto: "http").ConfigureAwait(true);

        Assert.Equal(_siteA, resolved);
        Assert.Equal(Stranger, _capture.RemoteAddress);
        Assert.NotEqual("http", _capture.Scheme);
    }

    /// <summary>
    /// A CDN in front of the proxy: both trusted, so the outermost hop's host wins. Before
    /// 009 only the nearest hop was unwound (research S-14).
    /// </summary>
    [Fact]
    public async Task Two_trusted_hops_use_the_outer_hops_host()
    {
        var resolved = await ResolveAsync(
            from: Proxy,
            host: "internal.local",
            forwardedHost: "b.test, a.test",
            forwardedFor: $"203.0.113.9, {Cdn}").ConfigureAwait(true);

        Assert.Equal(_siteB, resolved);
        Assert.Equal("203.0.113.9", _capture.RemoteAddress);
    }

    [Fact]
    public async Task An_untrusted_outer_hop_cannot_choose_the_site()
    {
        // The nearest hop is trusted and says the visitor asked for a.test; the hop before
        // it is a stranger claiming b.test. Unwinding stops at the stranger.
        var resolved = await ResolveAsync(
            from: Proxy,
            host: "internal.local",
            forwardedHost: "b.test, a.test",
            forwardedFor: $"203.0.113.9, {Stranger}").ConfigureAwait(true);

        Assert.Equal(_siteA, resolved);
        Assert.Equal(Stranger, _capture.RemoteAddress);
    }

    [Fact]
    public async Task A_spoofed_forwarded_host_on_a_direct_connection_reaches_nothing_it_names()
    {
        var response = await SendAsync(from: Stranger, host: "unknown.test", forwardedHost: "a.test").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> ResolveAsync(
        string from,
        string host,
        string? forwardedHost = null,
        string? forwardedFor = null,
        string? forwardedProto = null)
    {
        var response = await SendAsync(from, host, forwardedHost, forwardedFor, forwardedProto).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await SitesApi.BodyAsync(response).ConfigureAwait(false)).GetProperty("siteId").GetGuid();
    }

    private Task<HttpResponseMessage> SendAsync(
        string from,
        string host,
        string? forwardedHost = null,
        string? forwardedFor = null,
        string? forwardedProto = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Host = host;
        request.Headers.Add(IdentitySeed.RemoteAddressHeader, from);
        if (forwardedHost is not null)
        {
            request.Headers.Add("X-Forwarded-Host", forwardedHost);
        }

        // Every hop that forwards a host also forwards an address; the middleware unwinds
        // the lists together.
        request.Headers.Add("X-Forwarded-For", forwardedFor ?? "203.0.113.1");
        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        return _app.CreateAnonymousClient().SendAsync(request);
    }

    /// <summary>What the pipeline left on the last request.</summary>
    internal sealed class RequestCapture
    {
        public string? Scheme { get; set; }

        public string? RemoteAddress { get; set; }
    }

    private sealed class CaptureStartupFilter(RequestCapture capture) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                await nextMiddleware(context).ConfigureAwait(false);
                capture.Scheme = context.Request.Scheme;
                capture.RemoteAddress = context.Connection.RemoteIpAddress?.ToString();
            });

            next(app);
        };
    }
}
