using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// The trash of each kind (021 FR-029, SC-007): a trashed page, component, item and file of the
/// other site are sent to every trash endpoint under this site, and must not be found, listed,
/// previewed, planned or restored.
/// </summary>
internal sealed class TrashIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Trash";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/pages/sites/{siteId:guid}/trash/pages/{pageId:guid}",
        "GET api/v1/pages/sites/{siteId:guid}/trash/pages/{pageId:guid}/restore-plan",
        "POST api/v1/pages/sites/{siteId:guid}/trash/pages/{pageId:guid}/restore",
        "GET api/v1/pages/sites/{siteId:guid}/trash/components/{componentId:guid}",
        "GET api/v1/pages/sites/{siteId:guid}/trash/components/{componentId:guid}/restore-plan",
        "POST api/v1/pages/sites/{siteId:guid}/trash/components/{componentId:guid}/restore",
        "GET api/v1/cms/sites/{siteId:guid}/trash/items/{itemId:guid}",
        "GET api/v1/cms/sites/{siteId:guid}/trash/items/{itemId:guid}/restore-plan",
        "POST api/v1/cms/sites/{siteId:guid}/trash/items/{itemId:guid}/restore",
        "GET api/v1/media/sites/{siteId:guid}/trash/files/{fileId:guid}",
        "GET api/v1/media/sites/{siteId:guid}/trash/files/{fileId:guid}/restore-plan",
        "POST api/v1/media/sites/{siteId:guid}/trash/files/{fileId:guid}/restore",
        "DELETE api/v1/pages/sites/{siteId:guid}/trash/pages/{pageId:guid}",
        "DELETE api/v1/pages/sites/{siteId:guid}/trash/components/{componentId:guid}",
        "DELETE api/v1/cms/sites/{siteId:guid}/trash/items/{itemId:guid}",
        "DELETE api/v1/media/sites/{siteId:guid}/trash/files/{fileId:guid}",
    ];

    public string? NeverIssuedPublicPath => null;

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        var suffix = siteId.ToString("N")[^12..];

        var page = (await PagesApi.CreateOkAsync(admin, siteId, $"trash-probe-{suffix}").ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, siteId, page, 1).ConfigureAwait(false)).ConfigureAwait(false);

        var component = (await PagesApi.CreateComponentOkAsync(admin, siteId, $"{PagesApi.CanaryMarker}trash-{suffix}").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var componentVersion = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, siteId, component).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, siteId, component, componentVersion).ConfigureAwait(false)).ConfigureAwait(false);

        var created = await CmsApi.CreateOkAsync(admin, siteId, $"trash-probe-{suffix}").ConfigureAwait(false);
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 100 })),
        };
        var collection = created.GetProperty("id").GetGuid();
        var saved = await CmsApi.SaveOkAsync(admin, siteId, collection, CmsApi.SaveBody(created, schema)).ConfigureAwait(false);
        var item = await ItemsApi.CreateOkAsync(admin, siteId, collection,
            ItemsApi.Draft("trash-probe", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}trash" }, saved.GetProperty("schemaRevision").GetInt32())).ConfigureAwait(false);
        var itemId = ItemsApi.Id(item);
        await PagesApi.OkAsync(await ItemsApi.DeleteAsync(admin, siteId, collection, itemId, ItemsApi.Revision(item)).ConfigureAwait(false)).ConfigureAwait(false);

        var file = await MediaApi.UploadOkAsync(admin, siteId).ConfigureAwait(false);
        await PagesApi.OkAsync(await MediaApi.DeleteAsync(admin, siteId, file.Id).ConfigureAwait(false)).ConfigureAwait(false);

        return new SeededRecords([page.ToString(), component.ToString(), itemId.ToString(), file.Id.ToString()], []);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var entries = new (string Base, string Id)[]
        {
            ($"/api/v1/pages/sites/{siteId}/trash/pages", other.Ids[0]),
            ($"/api/v1/pages/sites/{siteId}/trash/components", other.Ids[1]),
            ($"/api/v1/cms/sites/{siteId}/trash/items", other.Ids[2]),
            ($"/api/v1/media/sites/{siteId}/trash/files", other.Ids[3]),
        };

        foreach (var (root, id) in entries)
        {
            yield return new ProbeRequest(HttpMethod.Get, root, ProbeKind.Listing);
            yield return new ProbeRequest(HttpMethod.Get, $"{root}/{id}", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Get, $"{root}/{id}/restore-plan", ProbeKind.Addressed);
            yield return new ProbeRequest(HttpMethod.Post, $"{root}/{id}/restore", ProbeKind.Addressed, new { confirmedAddress = "probe" });
            yield return new ProbeRequest(HttpMethod.Delete, $"{root}/{id}", ProbeKind.Addressed);
        }
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        // Each entry is still in this site's own trash.
        var paths = new[]
        {
            $"/api/v1/pages/sites/{siteId}/trash/pages/{seeded.Ids[0]}",
            $"/api/v1/pages/sites/{siteId}/trash/components/{seeded.Ids[1]}",
            $"/api/v1/cms/sites/{siteId}/trash/items/{seeded.Ids[2]}",
            $"/api/v1/media/sites/{siteId}/trash/files/{seeded.Ids[3]}",
        };

        foreach (var path in paths)
        {
            var response = await admin.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }
        }

        return true;
    }
}
