using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Isolation.Probes;

/// <summary>
/// Collection templates (016 FR-037, Principle V): another site's template page cannot be read,
/// changed or removed, a template cannot be made of another site's collection, and another
/// site's item addresses look like addresses never issued.
/// </summary>
/// <remarks>
/// The seeded site has a collection with one published item and a live template of it at
/// <c>/probe-blog/{slug}</c>, so the item has a public address. List previews join this probe
/// with US4.
/// </remarks>
internal sealed class TemplateIsolationProbe : ISiteIsolationProbe
{
    public string Module => "Templates";

    public IReadOnlyList<string> CoveredEndpoints { get; } =
    [
        "GET api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/template",
        "PUT api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/template",
        "DELETE api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/template",
    ];

    public string? NeverIssuedPublicPath => "/probe-blog/never-issued";

    public async Task<SeededRecords> SeedAsync(QuickBuilderApp app, HttpClient admin, Guid siteId, string hostName)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var created = await CmsApi.CreateOkAsync(admin, siteId, $"tpl-{siteId.ToString("N")[^12..]}").ConfigureAwait(false);
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 100 })),
        };
        var collectionId = created.GetProperty("id").GetGuid();
        var saved = await CmsApi.SaveOkAsync(admin, siteId, collectionId, CmsApi.SaveBody(created, schema)).ConfigureAwait(false);
        var item = await ItemsApi.CreateOkAsync(admin, siteId, collectionId,
            ItemsApi.Draft(ItemSlug(siteId), new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}probe" }, saved.GetProperty("schemaRevision").GetInt32())).ConfigureAwait(false);
        await ItemsApi.PublishOkAsync(admin, siteId, collectionId, ItemsApi.Id(item), ItemsApi.Revision(item)).ConfigureAwait(false);

        var blog = (await PagesApi.CreateOkAsync(admin, siteId, "probe-blog").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var template = (await PagesApi.CreateOkAsync(admin, siteId, "post", blog).ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await admin.PutAsJsonAsync(
            new Uri(TemplatePath(siteId, template), UriKind.Relative),
            new { collectionId, version = await VersionAsync(admin, siteId, template).ConfigureAwait(false) }).ConfigureAwait(false)).ConfigureAwait(false);

        var pagePath = PagesApi.PagePath(siteId, template);
        await PublishingApi.SaveAsync(admin, pagePath, PublishingApi.HeadingDocument("Probe post")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, pagePath).ConfigureAwait(false);

        return new SeededRecords([template.ToString(), collectionId.ToString()], [$"/probe-blog/{ItemSlug(siteId)}"]);
    }

    public IEnumerable<ProbeRequest> AdminRequests(Guid siteId, SeededRecords other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var template = TemplatePath(siteId, Guid.Parse(other.Ids[0]));
        var collectionId = Guid.Parse(other.Ids[1]);

        yield return new ProbeRequest(HttpMethod.Get, $"{template}?collectionId={collectionId}", ProbeKind.Addressed);
        yield return new ProbeRequest(HttpMethod.Put, template, ProbeKind.Addressed, new { collectionId, version = 1 });
        yield return new ProbeRequest(HttpMethod.Delete, $"{template}?version=1", ProbeKind.Addressed);
    }

    public async Task<bool> StillIntactAsync(HttpClient admin, Guid siteId, SeededRecords seeded)
    {
        ArgumentNullException.ThrowIfNull(seeded);

        var response = await PagesApi.GetAsync(admin, siteId, Guid.Parse(seeded.Ids[0])).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var page = await PagesApi.OkAsync(response).ConfigureAwait(false);
        return page.TryGetProperty("templateCollectionId", out var collection)
            && collection.ValueKind == System.Text.Json.JsonValueKind.String
            && collection.GetGuid() == Guid.Parse(seeded.Ids[1]);
    }

    private static string ItemSlug(Guid siteId) => $"item-{siteId.ToString("N")[^12..]}";

    private static string TemplatePath(Guid siteId, Guid pageId) => $"{PagesApi.PagePath(siteId, pageId)}/template";

    private static async Task<int> VersionAsync(HttpClient admin, Guid siteId, Guid pageId) =>
        (await PagesApi.OkAsync(await PagesApi.GetAsync(admin, siteId, pageId).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("version").GetInt32();
}
