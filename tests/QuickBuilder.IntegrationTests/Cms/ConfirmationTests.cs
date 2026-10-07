using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// The server, not the builder, decides that a lossy change was confirmed: the digest of the
/// preview the member saw must match what the change would do now (014 FR-023, research CS-08).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConfirmationTests(DatabaseFixture database)
{
    private static JsonObject Schema(params JsonObject[] fields) => new() { ["schemaVersion"] = 1, ["fields"] = new JsonArray(fields) };

    private static async Task<(CmsScenario Scenario, JsonElement Collection, JsonObject Removal, IReadOnlyList<Guid> Items)> RemovalAsync(DatabaseFixture database, string label)
    {
        var scenario = await CmsScenario.CreateAsync(database, label);
        var collection = await scenario.CollectionAsync("posts", Schema(
            CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
            CmsApi.Field("~2", "minutes", "number", settings: new { decimals = 0 })));
        var items = await ItemSeed.AddAsync(scenario, scenario.SiteA, collection.GetProperty("id").GetGuid(), 4);
        var removal = CmsApi.Schema(collection);
        CmsApi.Fields(removal).RemoveAt(1);
        return (scenario, collection, removal, items);
    }

    [Fact]
    public async Task When_the_counts_change_after_the_preview_the_save_asks_again_with_the_new_preview()
    {
        var (scenario, collection, removal, items) = await RemovalAsync(database, "cms-outdated");
        using (scenario)
        {
            var id = collection.GetProperty("id").GetGuid();
            var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal));

            // Someone empties one item's value: one fewer value would be removed.
            await scenario.EditItemAsync(items[0], values => values.Set("f2", null));

            var outdated = await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal, digest: preview.GetProperty("digest").GetString())));
            Assert.Equal((HttpStatusCode.Conflict, "cms.preview_outdated"), (outdated.Status, outdated.Code));
            var fresh = outdated.Body.GetProperty("preview");
            Assert.NotEqual(preview.GetProperty("digest").GetString(), fresh.GetProperty("digest").GetString());
            Assert.Equal(3, fresh.GetProperty("changes")[0].GetProperty("counts").GetProperty("cleared").GetInt32());

            await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal, digest: fresh.GetProperty("digest").GetString()));
        }
    }

    [Fact]
    public async Task When_items_change_but_the_counts_do_not_the_confirmation_still_holds()
    {
        var (scenario, collection, removal, items) = await RemovalAsync(database, "cms-same-counts");
        using (scenario)
        {
            var id = collection.GetProperty("id").GetGuid();
            var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal));

            await scenario.EditItemAsync(items[0], values => values.Set("f2", JsonValue.Create(42)));

            await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal, digest: preview.GetProperty("digest").GetString()));
        }
    }

    [Fact]
    public async Task A_made_up_digest_is_outdated_and_no_digest_is_a_missing_confirmation()
    {
        var (scenario, collection, removal, _) = await RemovalAsync(database, "cms-made-up");
        using (scenario)
        {
            var id = collection.GetProperty("id").GetGuid();

            Assert.Equal("cms.confirmation_required", (await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal)))).Code);
            Assert.Equal("cms.preview_outdated", (await PagesApi.ProblemAsync(await CmsApi.SaveAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, removal, digest: new string('0', 64))))).Code);
        }
    }

    [Fact]
    public async Task Free_and_reported_changes_need_no_confirmation()
    {
        var (scenario, collection, _, _) = await RemovalAsync(database, "cms-no-confirmation");
        using (scenario)
        {
            var id = collection.GetProperty("id").GetGuid();
            var schema = CmsApi.Schema(collection);
            CmsApi.Fields(schema)[1]!["name"] = "Reading time";
            CmsApi.Fields(schema)[1]!["max"] = 1;

            var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, schema));
            Assert.False(preview.GetProperty("requiresConfirmation").GetBoolean());
            Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => c.GetProperty("rule").GetString() == "reported");

            await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, schema));
        }
    }
}
