using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Publishing (012 FR-037, SC-005): publications, versions, dependencies, schedules, the
/// publish log and design versions, through every publishing endpoint, and the published
/// pages themselves on the public surface.
/// </summary>
/// <remarks>
/// Each site gets a published component, a published page showing it that links to a second
/// published page, a pending schedule, and a published design. Addresses end with the site id,
/// so each exists on exactly one site. Pages' own probe already sends another site's ids to
/// every publishing endpoint; this one adds the reads that could leak content — history,
/// versions, the log, the design versions — and the pages as visitors reach them.
/// </remarks>
internal sealed class PublishingIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Publishing";

    // Every endpoint here is also in PagesIsolationProbe's list; nothing new to account for.
    public IReadOnlyList<string> CoveredEndpoints { get; } = [];

    public string? NeverIssuedPublicPath => "/pub-neverissued";

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var suffix = siteId.ToString("N")[^10..];

        var component = (await PagesApi.CreateComponentOkAsync(admin, siteId, $"Probe {suffix}").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var componentPath = PagesApi.ComponentPath(siteId, component);
        await PublishingApi.SaveAsync(admin, componentPath, PublishingApi.ComponentDocument($"component-{suffix}")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, componentPath).ConfigureAwait(false);

        var linked = (await PagesApi.CreateOkAsync(admin, siteId, $"pub-{suffix}-linked").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var linkedPath = PagesApi.PagePath(siteId, linked);
        await PublishingApi.SaveAsync(admin, linkedPath, PublishingApi.HeadingDocument($"linked-{suffix}")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, linkedPath).ConfigureAwait(false);

        var page = (await PagesApi.CreateOkAsync(admin, siteId, $"pub-{suffix}").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var pagePath = PagesApi.PagePath(siteId, page);
        await PublishingApi.SaveAsync(admin, pagePath, PageDocument($"page-{suffix}", component, linked)).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, pagePath).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, pagePath).ConfigureAwait(false);

        var draftVersion = await PublishingApi.DraftVersionAsync(admin, pagePath).ConfigureAwait(false);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(admin, pagePath, "publish", DateTimeOffset.UtcNow.AddDays(1), draftVersion).ConfigureAwait(false)).ConfigureAwait(false);

        var tokens = await PagesApi.OkAsync(await admin.GetAsync(new Uri(DesignPath(siteId), UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);
        await PagesApi.OkAsync(await admin.PostAsJsonAsync(
            new Uri($"{DesignPath(siteId)}/publish", UriKind.Relative), new { version = tokens.GetProperty("version").GetUInt32() }).ConfigureAwait(false)).ConfigureAwait(false);

        return new SeededRecords(
            [page.ToString(), component.ToString(), linked.ToString(), $"page-{suffix}", $"component-{suffix}"],
            [$"/pub-{suffix}", $"/pub-{suffix}-linked"]);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var page = $"/api/v1/pages/sites/{siteId}/pages/{other.Ids[0]}";
        var component = $"/api/v1/pages/sites/{siteId}/components/{other.Ids[1]}";

        foreach (var subject in new[] { page, component })
        {
            yield return new ProbeRequest(HttpMethod.Get, $"{subject}/history", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Get, $"{subject}/versions/1", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Post, $"{subject}/versions/1/copy-to-draft", ProbeKind.Addressed, new { draftVersion = 0 });
            yield return new ProbeRequest(HttpMethod.Post, $"{subject}/rollback", ProbeKind.Addressed, new { versionNumber = 1 });
            yield return new ProbeRequest(HttpMethod.Delete, $"{subject}/schedules/publish", ProbeKind.Addressed);
        }

        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/publish-log", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/pages", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"/api/v1/pages/sites/{siteId}/components", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"{DesignPath(siteId)}/versions", ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, $"{DesignPath(siteId)}/versions/1", ProbeKind.Listing);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await PublishingApi.HistoryAsync(admin, PagesApi.PagePath(siteId, Guid.Parse(seeded.Ids[0]))).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var history = await PagesApi.OkAsync(response).ConfigureAwait(false);
        var publication = history.GetProperty("publication");
        return publication.GetProperty("liveNumber").GetInt32() == 2
            && publication.GetProperty("pendingSchedules").GetArrayLength() == 1
            && history.GetProperty("retainedVersions").GetArrayLength() == 2;
    }

    private static string DesignPath(Guid siteId) => $"/api/v1/editor/sites/{siteId}/design-tokens";

    /// <summary>A heading, the component, and a Button to the linked page.</summary>
    private static string PageDocument(string heading, Guid component, Guid linked) =>
        """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"ref","type":"component-ref","props":{"componentId":"""
        + JsonSerializer.Serialize(component.ToString())
        + """},"children":[]},{"id":"s1","type":"section","props":{},"children":[{"id":"h1","type":"heading","props":{"text":"""
        + JsonSerializer.Serialize(heading)
        + ""","level":1},"children":[]},{"id":"b1","type":"button","props":{"label":"Next","link":{"type":"page","pageId":"""
        + JsonSerializer.Serialize(linked.ToString())
        + """}},"children":[]}]}]}}""";
}
