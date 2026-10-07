using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// A schema change that stops part-way leaves the schema, its revision, history, references,
/// events and every item exactly as they were (014 SC-005, FR-021).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InterruptedSchemaChangeTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_fault_after_the_second_batch_of_ten_thousand_items_changes_nothing()
    {
        using var observer = new TestMigrationObserver();
        using var scenario = await CmsScenario.CreateAsync(database, "cms-interrupted", TestMigrationObserver.Install(observer));
        var people = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "people")).GetProperty("id").GetGuid();
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
                CmsApi.Field("~2", "minutes", "text", settings: new { multiline = false, maxLength = 256 })),
        };
        var collection = await scenario.CollectionAsync("posts", schema);
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 10_000, published: true);

        var before = await scenario.ItemsAsync(id);
        var eventsBefore = (await scenario.OutboxAsync()).Count;

        // Text to number, and a reference to People, in one save: every item is rewritten.
        var changed = CmsApi.Schema(collection);
        CmsApi.Fields(changed)[1] = CmsApi.Field("f2", "minutes", "number", settings: new { decimals = 0 });
        CmsApi.Fields(changed).Add(CmsApi.Field("~1", "author", "reference", settings: new { target = people }));
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, changed));

        observer.FailAfterBatch = 2;
        var response = await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, changed, digest: preview.GetProperty("digest").GetString()));
        Assert.False(response.IsSuccessStatusCode);

        var after = await CmsApi.GetOkAsync(scenario.Admin, scenario.SiteA, id);
        Assert.Equal(2, after.GetProperty("revision").GetInt32());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(collection.GetProperty("schema").GetRawText()), JsonNode.Parse(after.GetProperty("schema").GetRawText())));
        Assert.Equal(before, await scenario.ItemsAsync(id));
        Assert.Equal(eventsBefore, (await scenario.OutboxAsync()).Count);

        // Nothing was left locked: the same save now succeeds.
        observer.FailAfterBatch = null;
        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, changed, digest: preview.GetProperty("digest").GetString()));
        Assert.Equal(3, saved.GetProperty("revision").GetInt32());
    }
}
