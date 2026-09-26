using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Sites;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Host names are the Sites module's site-owned records. They have no public address of
/// their own, so this probe is administrative: removing another site's name through this
/// site's route must not find it, and this site's details must not list it.
/// </summary>
internal sealed class SitesIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Sites";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "DELETE api/v1/sites/{siteId:guid}/host-names/{hostName}",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var extra = $"extra-{Guid.NewGuid():N}.test";
        var response = await SitesApi.AddHostNameAsync(admin, siteId, extra).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new SeededRecords([hostName, extra], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var hostName in other.Ids)
        {
            yield return new ProbeRequest(HttpMethod.Delete, $"/api/v1/sites/{siteId}/host-names/{hostName}", ProbeKind.Addressed);
        }

        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/sites/{siteId}", ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var site = await SitesApi.GetOkAsync(admin, siteId).ConfigureAwait(false);
        var held = site.GetProperty("hostNames").EnumerateArray().Select(h => h.GetString()).ToHashSet();
        return seeded.Ids.All(held.Contains);
    }
}
