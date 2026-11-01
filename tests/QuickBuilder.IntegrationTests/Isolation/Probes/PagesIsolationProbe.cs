using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

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

        // 012: publishing (FR-037).
        "POST api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/publish",
        "POST api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/unpublish",
        "POST api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/rollback",
        "POST api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/publish",
        "POST api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/unpublish",
        "POST api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/rollback",
        "GET api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/history",
        "GET api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/versions/{number:int}",
        "POST api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/versions/{number:int}/copy-to-draft",
        "GET api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/history",
        "GET api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/versions/{number:int}",
        "POST api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/versions/{number:int}/copy-to-draft",
        "PUT api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/schedules/publish",
        "PUT api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/schedules/unpublish",
        "DELETE api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/schedules/publish",
        "DELETE api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/schedules/unpublish",
        "PUT api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/schedules/publish",
        "DELETE api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/schedules/publish",

        // 023: another site's page or component cannot be copied.
        "POST api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/copies",
        "POST api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/copies",
    ];

    /// <summary>012: an address shaped like each site's own published page, never issued.</summary>
    public string? NeverIssuedPublicPath => "/only-neverissued";

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var home = (await PagesApi.CreateOkAsync(admin, siteId, "home", isHome: true).ConfigureAwait(false)).GetProperty("id").GetGuid();
        var child = (await PagesApi.CreateOkAsync(admin, siteId, "news", home).ConfigureAwait(false)).GetProperty("id").GetGuid();
        var component = (await PagesApi.CreateComponentOkAsync(admin, siteId, "Header").ConfigureAwait(false)).GetProperty("id").GetGuid();

        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(
            admin, PagesApi.PagePath(siteId, child), PagesApi.DocumentReferencing(component)).ConfigureAwait(false)).ConfigureAwait(false);

        // 012: the component and the page that shows it are published, and one page has an
        // address only this site has, so another site's host can be asked for it.
        await PublishingApi.PublishCurrentAsync(admin, PagesApi.ComponentPath(siteId, component)).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(siteId, child)).ConfigureAwait(false);

        // The end of the id, not the start: a v7 id starts with its creation time, which two
        // sites created together share.
        var only = $"only-{siteId.ToString("N")[^10..]}";
        var onlyPage = (await PagesApi.CreateOkAsync(admin, siteId, only).ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PublishingApi.PublishCurrentAsync(admin, PagesApi.PagePath(siteId, onlyPage)).ConfigureAwait(false);

        return new SeededRecords([home.ToString(), child.ToString(), component.ToString()], [$"/{only}"]);
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

            // 012: another site's page can be neither published, taken down nor rolled back.
            yield return new ProbeRequest(HttpMethod.Post, $"{page}/publish", ProbeKind.Addressed, new { draftVersion = 0 });
            yield return new ProbeRequest(HttpMethod.Post, $"{page}/unpublish", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Post, $"{page}/rollback", ProbeKind.Addressed, new { versionNumber = 1 });
            yield return new ProbeRequest(HttpMethod.Get, $"{page}/history", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Get, $"{page}/versions/1", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Post, $"{page}/versions/1/copy-to-draft", ProbeKind.Addressed, new { draftVersion = 0 });
            yield return new ProbeRequest(HttpMethod.Put, $"{page}/schedules/publish", ProbeKind.Addressed, new { dueAtUtc = DateTimeOffset.UtcNow.AddDays(1), draftVersion = 0 });
            yield return new ProbeRequest(HttpMethod.Put, $"{page}/schedules/unpublish", ProbeKind.Addressed, new { dueAtUtc = DateTimeOffset.UtcNow.AddDays(1) });
            yield return new ProbeRequest(HttpMethod.Delete, $"{page}/schedules/publish", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Delete, $"{page}/schedules/unpublish", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Post, $"{page}/copies", ProbeKind.Addressed, new { document, baseSavedAtUtc = DateTimeOffset.UtcNow, title = "Probe" });
        }

        var component = $"/api/v1/pages/sites/{siteId}/components/{other.Ids[2]}";
        yield return new ProbeRequest(HttpMethod.Get, component, ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, component, ProbeKind.Addressed, new { name = "Probe", version = 0 });
        yield return new ProbeRequest(HttpMethod.Delete, $"{component}?version=0", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{component}/usages", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{component}/draft", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, $"{component}/draft", ProbeKind.Addressed, new { version = 0, document });
        yield return new ProbeRequest(HttpMethod.Post, $"{component}/publish", ProbeKind.Addressed, new { draftVersion = 0 });
        yield return new ProbeRequest(HttpMethod.Post, $"{component}/unpublish", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{component}/rollback", ProbeKind.Addressed, new { versionNumber = 1 });
        yield return new ProbeRequest(HttpMethod.Get, $"{component}/history", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{component}/versions/1", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{component}/versions/1/copy-to-draft", ProbeKind.Addressed, new { draftVersion = 0 });
        yield return new ProbeRequest(HttpMethod.Put, $"{component}/schedules/publish", ProbeKind.Addressed, new { dueAtUtc = DateTimeOffset.UtcNow.AddDays(1), draftVersion = 0 });
        yield return new ProbeRequest(HttpMethod.Delete, $"{component}/schedules/publish", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{component}/copies", ProbeKind.Addressed, new { document, baseSavedAtUtc = DateTimeOffset.UtcNow, name = "Probe" });

        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/pages", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/components", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/publish-log", ProbeKind.Listing);

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
        var news = await PagesApi.NodeAsync(admin, siteId, "/news").ConfigureAwait(false);

        return paths.SequenceEqual(["/", "/news", seeded.PublicPaths[0]])
            && body.GetProperty("name").GetString() == "Header"
            && body.GetProperty("usageCount").GetInt32() == 1
            && body.GetProperty("publication").GetProperty("liveNumber").GetInt32() == 1
            && news.GetProperty("publication").GetProperty("liveNumber").GetInt32() == 1;
    }
}
