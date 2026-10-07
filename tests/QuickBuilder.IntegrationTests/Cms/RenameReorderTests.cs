using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// Renaming and reordering never touch content: every stored value and every field id stays
/// exactly as it was (014 US2, SC-002, FR-008, FR-030).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RenameReorderTests(DatabaseFixture database)
{
    [Fact]
    public async Task Renames_rekeys_reorders_relabels_and_new_details_change_no_item_and_no_id()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-rename");
        var people = (await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "people")).GetProperty("id").GetGuid();
        var collection = await scenario.CollectionAsync("blog-posts", CmsApi.EveryType(people));
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 200, published: true);
        var before = await scenario.ItemsAsync(id);

        var schema = CmsApi.Schema(collection);
        var fields = CmsApi.Fields(schema);
        foreach (var field in fields)
        {
            field!["name"] = $"Renamed {field["key"]}";
            field["help"] = "Changed help.";
            field["key"] = $"new-{field["key"]}";
        }

        // Move the last field first, and reorder and relabel the category's options.
        var last = fields[^1]!;
        fields.RemoveAt(fields.Count - 1);
        fields.Insert(0, last);
        var options = fields.Single(f => f!["type"]!.GetValue<string>() == "option")!["options"]!.AsArray();
        options[0]!["label"] = "Company news";
        var first = options[0]!;
        options.RemoveAt(0);
        options.Add(first);

        // The builder saves free changes without a preview; the server agrees nothing needs one.
        var preview = await CmsApi.PreviewOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, schema, name: "Articles", slug: "articles"));
        Assert.False(preview.GetProperty("requiresConfirmation").GetBoolean());
        Assert.All(preview.GetProperty("changes").EnumerateArray(), c => Assert.Equal("free", c.GetProperty("rule").GetString()));

        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, schema, name: "Articles", slug: "articles"));

        Assert.Equal(before, await scenario.ItemsAsync(id));
        Assert.Equal(collection.GetProperty("revision").GetInt32() + 1, saved.GetProperty("revision").GetInt32());
        Assert.Equal(
            CmsApi.Fields(CmsApi.Schema(collection)).Select(f => f!["id"]!.GetValue<string>()).Order(),
            saved.GetProperty("schema").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("id").GetString()!).Order());
        Assert.Equal(("Articles", "articles"), (saved.GetProperty("name").GetString(), saved.GetProperty("slug").GetString()));
    }

    [Fact]
    public async Task A_new_field_with_a_removed_fields_key_gets_a_new_id_and_no_values()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-reuse-key");
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
                CmsApi.Field("~2", "summary", "text", settings: new { multiline = false, maxLength = 256 })),
        };
        var collection = await scenario.CollectionAsync("posts", schema);
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 5);

        // Remove "summary" (confirmed), then add a new "summary".
        var removed = CmsApi.Schema(collection);
        CmsApi.Fields(removed).RemoveAt(1);
        var (_, afterRemoval) = await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.SiteA, collection, removed);

        var readded = CmsApi.Schema(afterRemoval);
        CmsApi.Fields(readded).Add(CmsApi.Field("~1", "summary", "text", settings: new { multiline = false, maxLength = 256 }));
        var saved = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(afterRemoval, readded));

        Assert.Equal("f3", saved.GetProperty("schema").GetProperty("fields")[1].GetProperty("id").GetString());
        Assert.All(await scenario.ItemsAsync(id), i => Assert.DoesNotContain("\"f3\"", i.Draft, StringComparison.Ordinal));
    }
}
