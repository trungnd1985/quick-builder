using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Preview;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Preview links (022, Principle V): another site's link can be neither revoked nor listed, and
/// another site's page has no links to list or end.
/// </summary>
/// <remarks>
/// The seeded link is a live link to a page of its own site, so a request that wrongly reached it
/// would end it, and <see cref="StillIntactAsync"/> checks it is still listed.
/// </remarks>
internal sealed class PreviewLinksIsolationProbe : ISiteIsolationProbe
{
    public string Module => "PreviewLinks";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "POST api/v1/editor/sites/{siteId:guid}/preview-links/{linkId:guid}/revoke",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var page = (await PagesApi.CreateOkAsync(admin, siteId, "probe-preview").ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PublishingApi.SaveAsync(admin, PagesApi.PagePath(siteId, page), PublishingApi.HeadingDocument("Probe")).ConfigureAwait(false);
        var link = await PreviewLinksApi.CreateOkAsync(admin, siteId, "page", page, DateTimeOffset.UtcNow.AddDays(1)).ConfigureAwait(false);
        return new SeededRecords([link.Id.ToString(), page.ToString()], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var links = PreviewLinksApi.LinksPath(siteId);
        yield return new ProbeRequest(HttpMethod.Post, $"{links}/{other.Ids[0]}/revoke", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{links}/revoke-all", ProbeKind.Listing, new { documentKind = "page", documentId = other.Ids[1] });
        yield return new ProbeRequest(HttpMethod.Get, $"{links}/all", ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await PreviewLinksApi.ListAsync(admin, siteId, "page", Guid.Parse(seeded.Ids[1])).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return body.RootElement.EnumerateArray().Any(l => l.GetProperty("id").GetString() == seeded.Ids[0]);
    }
}
