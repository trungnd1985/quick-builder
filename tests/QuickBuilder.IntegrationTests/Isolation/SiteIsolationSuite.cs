using System.Net;
using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Sites;

namespace QuickBuilder.IntegrationTests.Isolation;

/// <summary>
/// The shared isolation suite (009 FR-025, FR-026, SC-003, User Story 5): for every module
/// that stores site-owned records, nothing of site B can be read, listed, changed or
/// deleted through site A, on either surface — and nothing of a disabled site is public.
/// </summary>
/// <remarks>
/// One theory, one case per registered probe. The probes know their module's API; the
/// suite knows the rules. A new module joins by adding a probe to
/// <see cref="IsolationProbes"/>, and <see cref="IsolationCoverageTests"/> fails until it does.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SiteIsolationSuite(DatabaseFixture database)
{
    [Theory]
    [MemberData(nameof(IsolationProbes.Names), MemberType = typeof(IsolationProbes))]
    public async Task One_sites_records_are_unreachable_through_another(string module)
    {
        var probe = IsolationProbes.Named(module);
        using var app = new QuickBuilderApp(await database.CreateDatabaseAsync($"isolation-{module.ToLowerInvariant()}").ConfigureAwait(true));
        var admin = await app.CreateAdminClientAsync().ConfigureAwait(true);

        var hostA = $"a-{Guid.NewGuid():N}.test";
        var hostB = $"b-{Guid.NewGuid():N}.test";
        var siteA = (await SitesApi.CreateOkAsync(admin, "Site A", [hostA]).ConfigureAwait(true)).GetProperty("id").GetGuid();
        var siteB = (await SitesApi.CreateOkAsync(admin, "Site B", [hostB]).ConfigureAwait(true)).GetProperty("id").GetGuid();

        await probe.SeedAsync(app, admin, siteA, hostA).ConfigureAwait(true);
        var recordsOfB = await probe.SeedAsync(app, admin, siteB, hostB).ConfigureAwait(true);

        // Administrative surface: B's records through A's route.
        foreach (var request in probe.AdminRequests(siteA, recordsOfB))
        {
            var message = new HttpRequestMessage(request.Method, new Uri(request.Path, UriKind.Relative));
            if (request.Body is not null)
            {
                message.Content = System.Net.Http.Json.JsonContent.Create(request.Body);
            }

            var response = await admin.SendAsync(message).ConfigureAwait(true);
            var body = await SitesApi.RawAsync(response).ConfigureAwait(true);

            if (request.Kind == ProbeKind.Addressed)
            {
                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{module}: {request.Method} {request.Path} answered {(int)response.StatusCode} for another site's record");
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var leaked = recordsOfB.Ids.FirstOrDefault(id => body.Contains(id, StringComparison.OrdinalIgnoreCase));
                Assert.True(leaked is null, $"{module}: {request.Method} {request.Path} lists another site's record {leaked}");
            }
        }

        Assert.True(await probe.StillIntactAsync(admin, siteB, recordsOfB).ConfigureAwait(true), $"{module}: a request through site A changed site B's records");

        // Public surface: B's addresses on A's host look like addresses never issued.
        if (probe.NeverIssuedPublicPath is { } neverIssued)
        {
            // Compared without traceId, which differs per request by design.
            var reference = await SignInHelpers.BodyWithoutTraceIdAsync(await SitesApi.PublicGetAsync(app, hostA, neverIssued).ConfigureAwait(true)).ConfigureAwait(true);
            foreach (var path in recordsOfB.PublicPaths)
            {
                var response = await SitesApi.PublicGetAsync(app, hostA, path).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal(reference, await SignInHelpers.BodyWithoutTraceIdAsync(response).ConfigureAwait(true));
            }
        }

        // FR-026: once B is disabled, none of its records is public, even on its own host.
        Assert.Equal(HttpStatusCode.OK, (await SitesApi.DisableAsync(admin, siteB).ConfigureAwait(true)).StatusCode);
        var unknownHost = await SitesApi.RawAsync(await SitesApi.PublicGetAsync(app, $"unknown-{Guid.NewGuid():N}.test").ConfigureAwait(true)).ConfigureAwait(true);
        foreach (var path in recordsOfB.PublicPaths.Append("/"))
        {
            var response = await SitesApi.PublicGetAsync(app, hostB, path).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(unknownHost, await SitesApi.RawAsync(response).ConfigureAwait(true));
        }
    }
}
