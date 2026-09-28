using System.Net;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Design token sets (011 FR-041). A site's tokens are addressed by the site alone, so the
/// probe seeds each site with a recognisable token id and checks that reading or counting on
/// the other site never shows it.
/// </summary>
/// <remarks>
/// Nothing in this feature is public — rendering is #12 — so the probe is administrative.
/// The seeded "id" is a token id, unique per site, which is what a leak would reveal.
/// </remarks>
internal sealed class EditorIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Editor";

    // Neither endpoint takes a record id beyond the site's, so none needs listing here.
    public IReadOnlyList<string> CoveredEndpoints { get; } = [];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var tokenId = $"color-probe-{siteId.ToString("N")[..12]}";
        await PagesApi.OkAsync(await EditorApi.SaveCurrentAsync(admin, siteId, EditorApi.DefaultWithColor(tokenId, "Probe")).ConfigureAwait(false)).ConfigureAwait(false);
        return new SeededRecords([tokenId], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        yield return new ProbeRequest(HttpMethod.Get, EditorApi.TokensPath(siteId), ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await EditorApi.GetAsync(admin, siteId).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var tokens = await PagesApi.OkAsync(response).ConfigureAwait(false);
        return tokens.GetProperty("document").GetProperty("colors").EnumerateArray()
            .Any(c => c.GetProperty("id").GetString() == seeded.Ids[0]);
    }
}
