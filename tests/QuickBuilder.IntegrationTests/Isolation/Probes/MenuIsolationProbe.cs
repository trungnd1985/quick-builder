using System.Net;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Menus (024 FR-035, Principle V): another site's menus cannot be read, changed, published,
/// deleted or listed, their drafts cannot be replaced, and asking which menus point at another
/// site's page names none of them.
/// </summary>
/// <remarks>
/// The seeded menu points at a page of its own site. A menu draft naming another site's page or
/// item is refused as an unknown target: <c>MenuEndpointsTests</c> covers it. A Navigation block
/// naming another site's menu cannot go live: <c>MenuPublishingTests</c> covers it.
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
        "GET api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/preview",
        "GET api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/usages",
        "POST api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/publish",
        "POST api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/unpublish",
        "POST api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/rollback",
        "PUT api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/schedules/publish",
        "DELETE api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/schedules/publish",
        "GET api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/history",
        "GET api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/versions/{number:int}",
        "POST api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/versions/{number:int}/copy-to-draft",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var page = (await PagesApi.CreateOkAsync(admin, siteId, "probe-menu-target").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var menu = await MenusApi.CreateIdAsync(admin, siteId, $"Probe menu {siteId.ToString("N")[^8..]}").ConfigureAwait(false);
        await MenusApi.SaveCurrentDraftOkAsync(admin, siteId, menu, MenusApi.Document(MenusApi.Entry("target", MenusApi.PageTarget(page)))).ConfigureAwait(false);

        // Published, so the version and history endpoints have something of this site to refuse.
        var draftVersion = (await MenusApi.GetOkAsync(admin, siteId, menu).ConfigureAwait(false)).GetProperty("draftVersion").GetUInt32();
        await PagesApi.OkAsync(await QuickBuilder.IntegrationTests.Publishing.PublishingApi.PublishAsync(admin, MenusApi.MenuPath(siteId, menu), draftVersion).ConfigureAwait(false)).ConfigureAwait(false);
        return new SeededRecords([menu.ToString(), page.ToString()], []);
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
        yield return new ProbeRequest(HttpMethod.Get, $"{one}/preview", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{one}/usages", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{one}/publish", ProbeKind.Addressed, new { draftVersion = 1 });
        yield return new ProbeRequest(HttpMethod.Post, $"{one}/unpublish", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{one}/rollback", ProbeKind.Addressed, new { versionNumber = 1 });
        yield return new ProbeRequest(HttpMethod.Put, $"{one}/schedules/publish", ProbeKind.Addressed, new { dueAtUtc = DateTimeOffset.UtcNow.AddDays(1), draftVersion = 1 });
        yield return new ProbeRequest(HttpMethod.Delete, $"{one}/schedules/publish", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{one}/history", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{one}/versions/1", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{one}/versions/1/copy-to-draft", ProbeKind.Addressed, new { draftVersion = 1 });
        yield return new ProbeRequest(HttpMethod.Get, MenusApi.MenusPath(siteId), ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"{MenusApi.MenusPath(siteId)}/references?pageId={other.Ids[1]}", ProbeKind.Listing);
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
