using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Pages, their drafts, components and component usages (010 FR-031, SC-006). Every
/// endpoint that addresses a page or a component is sent the other site's ids, with a body
/// valid enough that only the site decides the answer, and must not find them.
/// </summary>
/// <remarks>
/// Nothing in this feature is public — rendering is #12 — so the probe is administrative.
/// Seeded ids are, in order: the home page, a child page, and a component the child's draft
/// references.
/// </remarks>
internal sealed class PagesIsolationProbe : ISiteIsolationProbe
{
    private const string EmptyDocument = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[]}}""";

    public string Module => "Pages";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}",
        "PUT api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}",
        "DELETE api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}",
        "POST api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/move",
        "POST api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/make-home",
        "GET api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/draft",
        "PUT api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/draft",
        "GET api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}",
        "PUT api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}",
        "DELETE api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}",
        "GET api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/usages",
        "GET api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/draft",
        "PUT api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/draft",
        "GET api/v1/pages/sites/{siteId:guid}/token-usages/{tokenId}",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var home = (await PagesApi.CreateOkAsync(admin, siteId, "home", isHome: true).ConfigureAwait(false)).GetProperty("id").GetGuid();
        var child = (await PagesApi.CreateOkAsync(admin, siteId, "news", home).ConfigureAwait(false)).GetProperty("id").GetGuid();
        var component = (await PagesApi.CreateComponentOkAsync(admin, siteId, "Header").ConfigureAwait(false)).GetProperty("id").GetGuid();

        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(
            admin, PagesApi.PagePath(siteId, child), PagesApi.DocumentReferencing(component)).ConfigureAwait(false)).ConfigureAwait(false);

        return new SeededRecords([home.ToString(), child.ToString(), component.ToString()], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        using var empty = JsonDocument.Parse(EmptyDocument);
        var document = empty.RootElement.Clone();

        foreach (var pageId in other.Ids.Take(2))
        {
            var page = $"/api/v1/pages/sites/{siteId}/pages/{pageId}";
            yield return new ProbeRequest(HttpMethod.Get, page, ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Put, page, ProbeKind.Addressed, new { title = "Probe", slug = "probe", version = 1 });
            yield return new ProbeRequest(HttpMethod.Delete, $"{page}?version=1", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Post, $"{page}/move", ProbeKind.Addressed, new { parentId = (Guid?)null, afterPageId = (Guid?)null, version = 1 });
            yield return new ProbeRequest(HttpMethod.Post, $"{page}/make-home", ProbeKind.Addressed, new { version = 1 });
            yield return new ProbeRequest(HttpMethod.Get, $"{page}/draft", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Put, $"{page}/draft", ProbeKind.Addressed, new { version = 0, document });
        }

        var component = $"/api/v1/pages/sites/{siteId}/components/{other.Ids[2]}";
        yield return new ProbeRequest(HttpMethod.Get, component, ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, component, ProbeKind.Addressed, new { name = "Probe", version = 0 });
        yield return new ProbeRequest(HttpMethod.Delete, $"{component}?version=0", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{component}/usages", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{component}/draft", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, $"{component}/draft", ProbeKind.Addressed, new { version = 0, document });

        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/pages", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/components", ProbeKind.Listing);

        // 011: the token usage count reads every draft of the site, so it must not see the
        // other site's (TokenUsageSiteFilterTests has the counting half).
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/token-usages/color-brand", ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var paths = await PagesApi.PathsAsync(admin, siteId).ConfigureAwait(false);
        var component = await PagesApi.GetComponentAsync(admin, siteId, Guid.Parse(seeded.Ids[2])).ConfigureAwait(false);
        if (component.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var body = await PagesApi.OkAsync(component).ConfigureAwait(false);
        return paths.SequenceEqual(["/", "/news"])
            && body.GetProperty("name").GetString() == "Header"
            && body.GetProperty("usageCount").GetInt32() == 1;
    }
}
