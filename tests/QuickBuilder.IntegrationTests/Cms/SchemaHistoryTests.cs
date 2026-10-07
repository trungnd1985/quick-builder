using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// One history entry per saved revision, newest first, with who, when and counts — and
/// never an item value (014 US6, FR-033, research CS-13).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SchemaHistoryTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_save_is_one_entry_with_its_kinds_and_counts_and_no_item_values()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-history");
        var collection = await scenario.CollectionAsync("posts", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(
                CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 256 }),
                CmsApi.Field("~2", "minutes", "text", settings: new { multiline = false, maxLength = 256 })),
        });
        var id = collection.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(scenario, scenario.SiteA, id, 3);

        // Revision 3: a detail-only save. Revision 4: text to number, confirmed.
        var renamed = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, CmsApi.Schema(collection), name: "Articles"));
        var schema = CmsApi.Schema(renamed);
        CmsApi.Fields(schema)[1] = CmsApi.Field("f2", "minutes", "number", settings: new { decimals = 0 });
        var (preview, _) = await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.SiteA, renamed, schema);

        using var editor = await scenario.UserAsync((scenario.SiteA, SystemRole.Editor));
        var history = await PagesApi.OkAsync(await CmsApi.HistoryAsync(editor, scenario.SiteA, id));
        var entries = history.GetProperty("entries").EnumerateArray().ToList();

        Assert.Equal([4, 3, 2, 1], entries.Select(e => e.GetProperty("revision").GetInt32()));
        Assert.Equal("created", entries[3].GetProperty("changes")[0].GetProperty("kind").GetString());
        Assert.Equal("detailsChanged", entries[1].GetProperty("changes")[0].GetProperty("kind").GetString());

        var typeChange = entries[0].GetProperty("changes")[0];
        Assert.Equal(("typeChanged", "text", "number"), (typeChange.GetProperty("kind").GetString(), typeChange.GetProperty("from").GetString(), typeChange.GetProperty("to").GetString()));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(preview.GetProperty("changes")[0].GetProperty("counts").GetRawText()),
            JsonNode.Parse(typeChange.GetProperty("counts").GetRawText())));
        Assert.False(string.IsNullOrEmpty(entries[0].GetProperty("changedBy").GetProperty("displayName").GetString()));

        // No item value — every seeded text carries the canary — anywhere in the history.
        Assert.DoesNotContain("zz-canary-item", history.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_pages_by_revision()
    {
        using var scenario = await CmsScenario.CreateAsync(database, "cms-history-paging");
        var collection = await CmsApi.CreateOkAsync(scenario.Admin, scenario.SiteA, "posts");
        var id = collection.GetProperty("id").GetGuid();
        for (var i = 0; i < 4; i++)
        {
            collection = await CmsApi.SaveOkAsync(scenario.Admin, scenario.SiteA, id, CmsApi.SaveBody(collection, CmsApi.Schema(collection), name: $"Name {i}"));
        }

        var first = await PagesApi.OkAsync(await CmsApi.HistoryAsync(scenario.Admin, scenario.SiteA, id, "?limit=2"));
        Assert.Equal([5, 4], first.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("revision").GetInt32()));
        Assert.Equal(4, first.GetProperty("nextBefore").GetInt32());

        var second = await PagesApi.OkAsync(await CmsApi.HistoryAsync(scenario.Admin, scenario.SiteA, id, "?limit=2&before=4"));
        Assert.Equal([3, 2], second.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("revision").GetInt32()));

        var last = await PagesApi.OkAsync(await CmsApi.HistoryAsync(scenario.Admin, scenario.SiteA, id, "?limit=2&before=2"));
        Assert.Equal([1], last.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("revision").GetInt32()));
        Assert.Equal(JsonValueKind.Null, last.GetProperty("nextBefore").ValueKind);
    }
}
