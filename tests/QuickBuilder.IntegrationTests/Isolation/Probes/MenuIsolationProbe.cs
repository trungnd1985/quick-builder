using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Menus (024 FR-035, Principle V): another site's menus cannot be read, changed, published,
/// deleted or listed, and their drafts cannot be replaced.
/// </summary>
/// <remarks>
/// The seeded menu points at a page of its own site. A menu draft naming another site's page or
/// item is refused as an unknown target: <c>MenuEndpointsTests</c> covers it.
/// </remarks>
internal sealed class MenuIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Menus";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}",
        "PUT api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}",
        "DELETE api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}",
        "PUT api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/draft",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var page = (await PagesApi.CreateOkAsync(admin, siteId, "probe-menu-target").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var menu = await MenusApi.CreateIdAsync(admin, siteId, $"Probe menu {siteId.ToString("N")[^8..]}").ConfigureAwait(false);
        await MenusApi.SaveCurrentDraftOkAsync(admin, siteId, menu, MenusApi.Document(MenusApi.Entry("target", MenusApi.PageTarget(page)))).ConfigureAwait(false);
        return new SeededRecords([menu.ToString()], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var one = MenusApi.MenuPath(siteId, Guid.Parse(other.Ids[0]));
        yield return new ProbeRequest(HttpMethod.Get, one, ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, one, ProbeKind.Addressed, new { name = "Probe rename", version = 1 });
        yield return new ProbeRequest(HttpMethod.Delete, $"{one}?version=1", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, $"{one}/draft", ProbeKind.Addressed, new
        {
            document = new { schemaVersion = 1, entries = Array.Empty<object>() },
            version = 1,
        });
        yield return new ProbeRequest(HttpMethod.Get, MenusApi.MenusPath(siteId), ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await MenusApi.GetAsync(admin, siteId, Guid.Parse(seeded.Ids[0])).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var menu = await PagesApi.OkAsync(response).ConfigureAwait(false);
        return menu.GetProperty("entryCount").GetInt32() == 1;
    }
}
