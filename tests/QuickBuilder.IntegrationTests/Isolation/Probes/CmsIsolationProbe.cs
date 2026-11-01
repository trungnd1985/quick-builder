using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Collections, their schemas, history and references (014 Principle V). Every endpoint that
/// addresses a collection is sent the other site's id, with a body valid enough that only the
/// site decides the answer, and must not find it; the list must not show it.
/// </summary>
/// <remarks>
/// Nothing in this feature is public. The seeded collection has a title field, so it has a
/// history entry beyond its creation and a schema worth leaking, and one item (015), which
/// every item endpoint is sent with the other site's collection.
/// </remarks>
internal sealed class CmsIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Cms";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}",
        "PUT api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}",
        "DELETE api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/preview",
        "GET api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/history",
        "GET api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items",
        "GET api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/slug-availability",
        "GET api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}",
        "PUT api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}",
        "DELETE api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/publish",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/unpublish",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/duplicate",
        "GET api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/referrers",
        "GET api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/history",
        "PUT api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/schedules/{scheduleAction}",
        "DELETE api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/schedules/{scheduleAction}",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/versions/{number:int}/rollback",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/versions/{number:int}/copy-to-draft",
        "POST api/v1/cms/sites/{siteId:guid}/collections/{collectionId:guid}/items/{itemId:guid}/copies",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var created = await CmsApi.CreateOkAsync(admin, siteId, $"probe-{siteId.ToString("N")[^12..]}").ConfigureAwait(false);
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 100 })),
        };
        var collectionId = created.GetProperty("id").GetGuid();
        var saved = await CmsApi.SaveOkAsync(admin, siteId, collectionId, CmsApi.SaveBody(created, schema)).ConfigureAwait(false);
        var item = await ItemsApi.CreateOkAsync(admin, siteId, collectionId,
            ItemsApi.Draft("probe", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}probe" }, saved.GetProperty("schemaRevision").GetInt32())).ConfigureAwait(false);
        return new SeededRecords([collectionId.ToString(), ItemsApi.Id(item).ToString()], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var id = Guid.Parse(other.Ids[0]);
        var body = new
        {
            revision = 2,
            name = "Probe",
            slug = "probe",
            schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray() },
        };

        yield return new ProbeRequest(HttpMethod.Get, CmsApi.CollectionsPath(siteId), ProbeKind.Listing);
        yield return new ProbeRequest(HttpMethod.Get, CmsApi.CollectionPath(siteId, id), ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, CmsApi.CollectionPath(siteId, id), ProbeKind.Addressed, body);
        yield return new ProbeRequest(HttpMethod.Delete, $"{CmsApi.CollectionPath(siteId, id)}?revision=2", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, CmsApi.PreviewPath(siteId, id), ProbeKind.Addressed, body);
        yield return new ProbeRequest(HttpMethod.Get, CmsApi.HistoryPath(siteId, id), ProbeKind.Addressed);

        // 015: items of the other site's collection, under this site.
        var itemId = Guid.Parse(other.Ids[1]);
        var item = ItemsApi.ItemPath(siteId, id, itemId);
        var draft = ItemsApi.Draft("probe", new JsonObject { ["f1"] = "Probe" }, 2, revision: 1);
        var publishing = new { revision = 1, cancelSchedule = true };

        yield return new ProbeRequest(HttpMethod.Get, ItemsApi.ItemsPath(siteId, id), ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, ItemsApi.ItemsPath(siteId, id), ProbeKind.Addressed, ItemsApi.Draft("probe-2", new JsonObject { ["f1"] = "Probe" }, 2));
        yield return new ProbeRequest(HttpMethod.Get, $"{ItemsApi.ItemsPath(siteId, id)}/slug-availability?slug=probe", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, item, ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, item, ProbeKind.Addressed, draft);
        yield return new ProbeRequest(HttpMethod.Delete, $"{item}?revision=1", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{item}/publish", ProbeKind.Addressed, publishing);
        yield return new ProbeRequest(HttpMethod.Post, $"{item}/unpublish", ProbeKind.Addressed, publishing);
        yield return new ProbeRequest(HttpMethod.Post, $"{item}/duplicate", ProbeKind.Addressed, new { });
        yield return new ProbeRequest(HttpMethod.Get, $"{item}/referrers", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Get, $"{item}/history", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, $"{item}/schedules/publish", ProbeKind.Addressed, new { dueAtUtc = DateTimeOffset.UtcNow.AddHours(1), revision = 1 });
        yield return new ProbeRequest(HttpMethod.Delete, $"{item}/schedules/publish?revision=1", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Post, $"{item}/versions/1/rollback", ProbeKind.Addressed, publishing);
        yield return new ProbeRequest(HttpMethod.Post, $"{item}/versions/1/copy-to-draft", ProbeKind.Addressed, new { revision = 1 });
        yield return new ProbeRequest(HttpMethod.Post, $"{item}/copies", ProbeKind.Addressed, new { slug = "probe-copy", values = new JsonObject { ["f1"] = "Probe" }, baseSavedAtUtc = DateTimeOffset.UtcNow });
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await CmsApi.GetAsync(admin, siteId, Guid.Parse(seeded.Ids[0])).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var collection = await PagesApi.OkAsync(response).ConfigureAwait(false);
        if (collection.GetProperty("revision").GetInt32() != 2)
        {
            return false;
        }

        var items = await PagesApi.OkAsync(await ItemsApi.ListAsync(admin, siteId, Guid.Parse(seeded.Ids[0])).ConfigureAwait(false)).ConfigureAwait(false);
        var item = await ItemsApi.GetAsync(admin, siteId, Guid.Parse(seeded.Ids[0]), Guid.Parse(seeded.Ids[1])).ConfigureAwait(false);
        return items.GetProperty("total").GetInt32() == 1
            && item.StatusCode == HttpStatusCode.OK
            && ItemsApi.Revision(await PagesApi.OkAsync(item).ConfigureAwait(false)) == 1;
    }
}
