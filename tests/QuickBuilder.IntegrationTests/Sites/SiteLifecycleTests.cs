using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Sites;

/// <summary>
/// User Story 1: an operator goes from nothing to a working installation.
/// </summary>
/// <remarks>
/// Covers US1/AC1–AC3, SC-001, SC-001a, SC-002, FR-014 and FR-014a against a real
/// PostgreSQL instance and the real application pipeline.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SiteLifecycleTests(DatabaseFixture database) : IAsyncLifetime
{
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("lifecycle").ConfigureAwait(false);
        _app = new QuickBuilderApp(connectionString);

        // Forces the host to build and run startup migrations against the fresh database.
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US1/AC1 + SC-002: a cold start against an empty database becomes ready.</summary>
    [Fact]
    public async Task Cold_start_on_an_empty_database_reaches_ready()
    {
        var client = _app.CreateClient();
        var started = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // A generous ceiling on purpose. Shared CI runners are slower and noisier than the
        // reference two-core environment, so a tight 60s assertion would flake without
        // catching anything real. SC-002's actual budget is measured against the reference
        // environment in the quickstart run; this guards against an order-of-magnitude
        // regression.
        Assert.True(
            elapsed < TimeSpan.FromSeconds(120),
            $"Readiness took {elapsed.TotalSeconds:F1}s, which suggests a startup regression.");
    }

    /// <summary>US1/AC2 + FR-014a: a fresh installation has no sites, and that is normal.</summary>
    [Fact]
    public async Task Fresh_installation_returns_an_empty_site_list()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        var response = await client.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", body.Trim());
    }

    /// <summary>US1/AC3 + SC-001a: create a site, then reach it by its host name.</summary>
    [Fact]
    public async Task Created_site_is_reachable_by_its_host_name()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        var create = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Example", hostNames = new[] { "created-site.test" } })
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        var siteId = created.GetProperty("id").GetGuid();

        // Reachable before anything is published: the site answers with its own not-found page.
        var publicClient = _app.CreateClientForHost("created-site.test");
        Assert.True(await PublicSite.IsResolvedAsync(
            await publicClient.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true));

        // And once its home page is published, that is what the host name serves (012).
        await PublicSite.MarkAsync(client, siteId).ConfigureAwait(true);
        var resolved = await publicClient.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(siteId, await PublicSite.ResolvedSiteAsync(resolved).ConfigureAwait(true));
    }

    /// <summary>
    /// FR-014: an unknown host is rejected, and the response discloses nothing.
    /// </summary>
    /// <remarks>
    /// The assertion that matters is not the 404 — it is that the body is byte-identical
    /// whether the installation has sites or not. A response that differs would let an
    /// unauthenticated caller probe which sites exist.
    /// </remarks>
    [Fact]
    public async Task Unknown_host_is_rejected_identically_whether_or_not_sites_exist()
    {
        var beforeAnySites = _app.CreateClientForHost("nobody.test");
        var first = await beforeAnySites.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        var firstBody = await first.Content.ReadAsStringAsync().ConfigureAwait(true);

        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        await admin
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Decoy", hostNames = new[] { "decoy.test" } })
            .ConfigureAwait(true);

        var afterSites = _app.CreateClientForHost("nobody.test");
        var second = await afterSites.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true);
        var secondBody = await second.Content.ReadAsStringAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.DoesNotContain("decoy", secondBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A host name may belong to only one site, and the collision is an expected failure.
    /// </summary>
    /// <remarks>
    /// The unique index is the only place that can see other sites' host names, so this
    /// proves the handler translates the constraint violation into a machine-readable
    /// error (FR-009) rather than letting it escape as an unhandled exception.
    /// </remarks>
    [Fact]
    public async Task Duplicate_host_name_is_rejected_as_a_conflict()
    {
        var client = await _app.CreateAdminClientAsync().ConfigureAwait(true);

        await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "First", hostNames = new[] { "contested.test" } })
            .ConfigureAwait(true);

        var second = await client
            .PostAsJsonAsync(
                new Uri("/api/v1/sites", UriKind.Relative),
                new { name = "Second", hostNames = new[] { "contested.test" } })
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.Equal("sites.host_name_taken", problem.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// FR-011b: the administrative surface works with no resolved site.
    /// </summary>
    /// <remarks>
    /// The default client sends a Host header that matches no site. Admin requests must be
    /// unaffected by that — otherwise a fresh installation would be unreachable, which is
    /// the whole point of the admin/public split.
    /// </remarks>
    [Fact]
    public async Task Admin_surface_works_without_a_resolved_site()
    {
        var client = await _app.CreateAdminClientAsync("unrelated.test").ConfigureAwait(true);

        var sites = await client.GetAsync(new Uri("/api/v1/sites", UriKind.Relative)).ConfigureAwait(true);
        var health = await client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, sites.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
