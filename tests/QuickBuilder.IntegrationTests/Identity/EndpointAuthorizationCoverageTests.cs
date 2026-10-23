using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// SC-002: 100% of administrative operations refuse an unauthenticated caller — every
/// endpoint the running application has, not a hand-picked sample (research R-06).
/// </summary>
/// <remarks>
/// The architecture test proves every action <em>declares</em> its access; this proves the
/// declarations are <em>enforced</em>. A new endpoint is covered the moment it exists.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class EndpointAuthorizationCoverageTests(DatabaseFixture database) : IAsyncLifetime
{
    /// <summary>contracts/authorization.md §4, the /api part.</summary>
    private static readonly HashSet<string> AnonymousAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET api/v1/identity/status",
        "POST api/v1/identity/setup",
        "POST api/v1/identity/sign-in",
        "POST api/v1/identity/set-password-links/check",
        "POST api/v1/identity/set-password",
    };

    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("coverage").ConfigureAwait(false));
        _ = _app.Services;

        // Claimed, so the setup gate is out of the way and authorization is what answers.
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Every_admin_endpoint_refuses_an_anonymous_caller()
    {
        var anonymous = _app.CreateAnonymousClient();
        var endpoints = EndpointCatalog.Under(_app, "/api");
        var failures = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var key = $"{endpoint.Method} {endpoint.Pattern.TrimStart('/')}";
            var response = await EndpointCatalog.CallAsync(anonymous, endpoint).ConfigureAwait(true);

            var allowed = AnonymousAllowList.Contains(key);
            if (!allowed && response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{endpoint} answered {(int)response.StatusCode} to an anonymous caller; expected 401.");
            }
            else if (allowed && response.StatusCode == HttpStatusCode.Unauthorized)
            {
                failures.Add($"{endpoint} is on the anonymous allow-list but refused an anonymous caller.");
            }
        }

        Assert.True(endpoints.Count > 5, $"Only {endpoints.Count} endpoints were found; the enumeration is broken.");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    /// The public surface is exactly the reviewed anonymous endpoints: 003's media files and
    /// 012's pages and block stylesheet, 017's sitemap and robots.txt, and 018's form submissions
    /// (contracts/authorization.md §4, 003 FR-026, 012 FR-022, 017 FR-023, FR-025, 018 FR-015).
    /// 001's site placeholder at <c>/</c> is gone: published pages replaced it. A new public
    /// endpoint fails here until someone adds it on purpose.
    /// </summary>
    [Fact]
    public void The_public_surface_is_exactly_the_reviewed_endpoints()
    {
        var reserved = new[] { "/api", "/admin", "/health" };
        var publicEndpoints = EndpointCatalog.Under(_app, "/")
            .Where(e => !reserved.Any(prefix => ("/" + e.Pattern.TrimStart('/')).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(e => $"{e.Method} /{e.Pattern.TrimStart('/')}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "DELETE /_forms/{**rest}",
                "DELETE /{**path:publicPath}",
                "GET /_assets/{**file}",
                "GET /_forms/{**rest}",
                "GET /_media/{**file}",
                "GET /robots.txt",
                "GET /sitemap-{number:int:min(1)}.xml",
                "GET /sitemap.xml",
                "GET /{**path:publicPath}",
                "HEAD /_assets/{**file}",
                "HEAD /_forms/{**rest}",
                "HEAD /_media/{**file}",
                "HEAD /robots.txt",
                "HEAD /sitemap-{number:int:min(1)}.xml",
                "HEAD /sitemap.xml",
                "HEAD /{**path:publicPath}",
                "PATCH /_forms/{**rest}",
                "PATCH /{**path:publicPath}",
                "POST /_forms/{formId}",
                "POST /{**path:publicPath}",
                "PUT /_forms/{**rest}",
                "PUT /{**path:publicPath}",
            ],
            publicEndpoints);
    }
}
