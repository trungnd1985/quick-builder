using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>
/// Page schema rule 14: a Collection list names a collection of the document's own site,
/// checked when a draft is saved (016 research TB-06, FR-037).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DraftCollectionCheckTests(DatabaseFixture database)
{
    private static string ListOf(Guid collectionId) =>
        "{\"id\":\"l1\",\"type\":\"collection-list\",\"props\":{\"source\":{\"collectionId\":\"" + collectionId
        + "\"}},\"children\":[{\"id\":\"l1-card\",\"type\":\"list-card\",\"props\":{},\"children\":[]},{\"id\":\"l1-empty\",\"type\":\"list-empty\",\"props\":{},\"children\":[]}]}";

    [Fact]
    public async Task A_list_of_this_sites_collection_is_saved()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "rule14-ok");
        var page = await world.PageAsync("home-list");

        var response = await PagesApi.SaveCurrentDraftAsync(world.Admin, PagesApi.PagePath(world.Site, page), TemplatesWorld.Document(ListOf(world.Posts)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_list_of_an_unknown_or_another_sites_collection_is_refused_alike()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "rule14-refused");
        var page = await world.PageAsync("home-list");
        var otherSite = (await world.Cms.CollectionAsync("elsewhere", new System.Text.Json.Nodes.JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new System.Text.Json.Nodes.JsonArray(Cms.CmsApi.Field("~1", "name", "text", settings: new { multiline = false, maxLength = 100 })),
        }, world.Cms.SiteB)).GetProperty("id").GetGuid();

        foreach (var collection in new[] { Guid.NewGuid(), otherSite })
        {
            var response = await PagesApi.SaveCurrentDraftAsync(world.Admin, PagesApi.PagePath(world.Site, page), TemplatesWorld.Document(ListOf(collection)));
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("pages.invalid_document", body.GetProperty("errorCode").GetString());
            Assert.Equal("unknown_collection", body.GetProperty("rule").GetString());
            Assert.Equal("/root/children/0/children/0/props/source", body.GetProperty("pointer").GetString());
        }
    }
}
