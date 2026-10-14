using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Seo;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Redirects, the sitemap and robots.txt (017 FR-038, Principle V): another site's redirects
/// cannot be read, changed, deleted, listed or exported, and another site's redirect sources
/// look like addresses never issued.
/// </summary>
/// <remarks>
/// The seeded site has a live page and a redirect to it from <c>/probe-old-…</c>, so the
/// redirect's source is a public address of that site only. A redirect target naming another
/// site's page is refused as unknown: <c>RedirectEndpointsTests</c> covers it.
/// </remarks>
internal sealed class SeoIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Redirects";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/pages/sites/{siteId:guid}/redirects/{redirectId:guid}",
        "PUT api/v1/pages/sites/{siteId:guid}/redirects/{redirectId:guid}",
        "DELETE api/v1/pages/sites/{siteId:guid}/redirects/{redirectId:guid}",
    ];

    public string? NeverIssuedPublicPath => "/probe-old-never-issued";

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var page = (await PagesApi.CreateOkAsync(admin, siteId, "probe-target").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var pagePath = PagesApi.PagePath(siteId, page);
        await PublishingApi.SaveAsync(admin, pagePath, PublishingApi.HeadingDocument("Probe target")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, pagePath).ConfigureAwait(false);

        var source = $"/probe-old-{siteId.ToString("N")[^12..]}";
        var redirect = await RedirectsApi.CreateOkAsync(admin, siteId, source, RedirectsApi.ToPage(page)).ConfigureAwait(false);
        return new SeededRecords([redirect.GetProperty("id").GetString()!], [source]);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var one = $"{RedirectsApi.Root(siteId)}/{other.Ids[0]}";
        yield return new ProbeRequest(HttpMethod.Get, one, ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, one, ProbeKind.Addressed, new
        {
            source = "/probe-changed",
            match = "exact",
            target = RedirectsApi.ToPath("/elsewhere"),
            statusCode = 301,
            enabled = true,
            version = 1,
        });
        yield return new ProbeRequest(HttpMethod.Delete, $"{one}?version=1", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, RedirectsApi.Root(siteId), ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"{RedirectsApi.Root(siteId)}/export", ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await RedirectsApi.GetAsync(admin, siteId, Guid.Parse(seeded.Ids[0])).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.OK;
    }
}
