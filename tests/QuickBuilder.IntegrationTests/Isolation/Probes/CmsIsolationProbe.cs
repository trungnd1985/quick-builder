using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Collections, their schemas, history and references (014 Principle V). Every endpoint that
/// addresses a collection is sent the other site's id, with a body valid enough that only the
/// site decides the answer, and must not find it; the list must not show it.
/// </summary>
/// <remarks>
/// Nothing in this feature is public. Items have no endpoint until #15; the item store is
/// covered by <c>CmsSiteFilterTests</c>. The seeded collection has a field, so it has a
/// history entry beyond its creation and a schema worth leaking.
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
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var created = await CmsApi.CreateOkAsync(admin, siteId, $"probe-{siteId.ToString("N")[^12..]}").ConfigureAwait(false);
        var schema = new JsonObject { ["schemaVersion"] = 1, ["fields"] = new JsonArray(CmsApi.Field("~1", "flag", "boolean")) };
        await CmsApi.SaveOkAsync(admin, siteId, created.GetProperty("id").GetGuid(), CmsApi.SaveBody(created, schema)).ConfigureAwait(false);
        return new SeededRecords([created.GetProperty("id").GetGuid().ToString()], []);
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
        return collection.GetProperty("revision").GetInt32() == 2;
    }
}
