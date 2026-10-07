using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Deleting collections: permanent, with their items and history, and refused while another
/// collection refers to them (014 US5, FR-004, research CS-11, clarification Q5).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CollectionReferenceTests(DatabaseFixture database)
{
    private static JsonObject Schema(params JsonObject[] fields) => new() { ["schemaVersion"] = 1, ["fields"] = new JsonArray(fields) };

    [Fact]
    public async Task A_collection_and_everything_in_it_is_deleted_and_its_address_name_is_free_again()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-delete");
        var collection = await scenario.CollectionAsync("jobs", Schema(CmsApi.Field("~1", "flag", "boolean")));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 14, published: true);

        var deleted = await CmsApi.DeleteAsync(scenario.Admin, scenario.SiteA, id, collection.GetProperty("revision").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CmsApi.GetAsync(scenario.Admin, scenario.SiteA, id)).StatusCode);
        Assert.Empty(await scenario.ItemsAsync(id));
        Assert.Equal("Cms.CollectionDeleted", (await scenario.OutboxAsync())[^1].Type);
        await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "jobs");
    }

    [Fact]
    public async Task A_collection_another_one_refers_to_cannot_be_deleted_until_the_reference_goes()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-referenced");
        var jobs = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "jobs", "Job openings");
        var jobsId = jobs.GetProperty("id").GetGuid();
        var team = await scenario.CollectionAsync("team", Schema(
            CmsApi.Field("~1", "name", "text", settings: new { multiline = false, maxLength = 100 }),
            CmsApi.Field("~2", "opening", "reference", settings: new { target = jobsId })));

        var refused = await PagesApi.ProblemAsync(await CmsApi.DeleteAsync(scenario.Admin, scenario.SiteA, jobsId, 1));
        Assert.Equal((HttpStatusCode.Conflict, "cms.collection_referenced"), (refused.Status, refused.Code));
        var reference = refused.Body.GetProperty("references")[0];
        Assert.Equal(
            (team.GetProperty("id").GetGuid(), "f2", "opening"),
            (reference.GetProperty("collectionId").GetGuid(), reference.GetProperty("fieldId").GetString(), reference.GetProperty("fieldName").GetString()));

        var withoutReference = CmsApi.Schema(team);
        CmsApi.Fields(withoutReference).RemoveAt(1);
        await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, team.GetProperty("id").GetGuid(), CmsApi.SaveBody(team, withoutReference));

        Assert.Equal(HttpStatusCode.NoContent, (await CmsApi.DeleteAsync(scenario.Admin, scenario.SiteA, jobsId, 1)).StatusCode);
    }

    [Fact]
    public async Task A_collection_that_refers_only_to_itself_can_be_deleted()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-self");
        var created = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var id = created.GetProperty("id").GetGuid();
        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(created, Schema(CmsApi.Field("~1", "related", "multiReference", settings: new { target = id }))));

        Assert.Equal(HttpStatusCode.NoContent, (await CmsApi.DeleteAsync(scenario.Admin, scenario.SiteA, id, saved.GetProperty("revision").GetInt32())).StatusCode);
    }

    [Fact]
    public async Task A_stale_revision_and_an_editor_are_refused()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-delete-refused");
        var collection = await scenario.CollectionAsync("posts", Schema(CmsApi.Field("~1", "flag", "boolean")));
        var id = collection.GetProperty("id").GetGuid();
        using var editor = await scenario.UserAsync((scenario.SiteA, SystemRole.Editor));

        Assert.Equal("cms.revision_conflict", (await PagesApi.ProblemAsync(await CmsApi.DeleteAsync(scenario.Admin, scenario.SiteA, id, 1))).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await CmsApi.DeleteAsync(editor, scenario.SiteA, id, 2)).StatusCode);
    }

    [Fact]
    public async Task A_delete_racing_a_save_that_adds_a_reference_to_it_has_one_winner()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-delete-race");
        var jobsId = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "jobs")).GetProperty("id").GetGuid();
        var team = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "team");
        var adding = CmsApi.SaveBody(team, Schema(CmsApi.Field("~1", "opening", "reference", settings: new { target = jobsId })));

        var save = CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, team.GetProperty("id").GetGuid(), adding);
        var delete = CmsApi.DeleteAsync(scenario.Admin, scenario.SiteA, jobsId, 1);
        var (saved, deleted) = (await save, await delete);

        var jobsExists = (await CmsApi.GetAsync(scenario.Admin, scenario.SiteA, jobsId)).StatusCode == HttpStatusCode.OK;
        if (saved.IsSuccessStatusCode)
        {
            // The reference landed first: the delete was refused, and jobs is still there.
            Assert.False(deleted.IsSuccessStatusCode);
            Assert.True(jobsExists);
        }
        else
        {
            Assert.Equal("cms.unknown_target_collection", (await PagesApi.ProblemAsync(saved)).Code);
            Assert.True(deleted.IsSuccessStatusCode);
            Assert.False(jobsExists);
        }
    }
}
