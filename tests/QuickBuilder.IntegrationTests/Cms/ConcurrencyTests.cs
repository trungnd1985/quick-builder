using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Two people changing one collection: the second save from an old revision is a conflict,
/// and anything that arrives while a change holds the collection is told it is busy
/// (014 US6, FR-022, research CS-09).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConcurrencyTests(DatabaseFixture database)
{
    [Fact]
    public async Task Two_saves_from_the_same_revision_give_one_success_and_one_conflict()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-two-saves");
        var collection = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var id = collection.GetProperty("id").GetGuid();

        var first = CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, CmsApi.Schema(collection), name: "First"));
        var second = CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, CmsApi.Schema(collection), name: "Second"));
        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = await PagesApi.ProblemAsync(responses.Single(r => r.StatusCode != HttpStatusCode.OK));
        Assert.Contains(loser.Code, new[] { "cms.revision_conflict", "cms.collection_busy" });
    }

    [Fact]
    public async Task A_save_and_a_delete_while_a_large_change_runs_are_busy()
    {
        using var observer = new TestMigrationObserver();
        using var scenario = await CmsScenario.CreateAsync(database, "cms-busy", TestMigrationObserver.Install(observer));
        var collection = await scenario.CollectionAsync("posts", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
                CmsApi.Field("~2", "flag", "boolean")),
        });
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 10_000);

        var removal = CmsApi.Schema(collection);
        CmsApi.Fields(removal).RemoveAt(1);
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal));
        observer.HoldAfterBatch = 3;
        var running = CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal, digest: preview.GetProperty("digest").GetString()));
        await observer.WaitUntilHeldAsync();

        try
        {
            var save = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, CmsApi.Schema(collection), name: "Other")));
            var delete = await PagesApi.ProblemAsync(await CmsApi.DeleteAsync(scenario.Admin, scenario.SiteA, id, collection.GetProperty("revision").GetInt32()));

            Assert.Equal((HttpStatusCode.Conflict, "cms.collection_busy"), (save.Status, save.Code));
            Assert.Equal((HttpStatusCode.Conflict, "cms.collection_busy"), (delete.Status, delete.Code));

            // Reading is never blocked.
            Assert.Equal(HttpStatusCode.OK, (await CmsApi.GetAsync(scenario.Admin, scenario.SiteA, id)).StatusCode);
        }
        finally
        {
            observer.Release();
        }

        Assert.Equal(HttpStatusCode.OK, (await running).StatusCode);
    }
}
