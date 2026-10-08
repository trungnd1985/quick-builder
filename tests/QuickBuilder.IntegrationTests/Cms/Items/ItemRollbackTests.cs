using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>History and rollback (015 US7, FR-022, FR-031).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemRollbackTests(DatabaseFixture database)
{
    [Fact]
    public async Task Rolling_back_publishes_an_earlier_version_as_a_new_one_and_leaves_the_draft()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-rollback");
        var item = await scenario.PostAsync("prices");
        var id = ItemsApi.Id(item);
        foreach (var title in new[] { "zz-canary-v1", "zz-canary-v2", "zz-canary-v3" })
        {
            item = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
                ItemsApi.Draft("prices", new JsonObject { ["f1"] = title }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)));
            item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        }

        var rolledBack = await PagesApi.OkAsync(await ItemsApi.RollbackAsync(scenario.Admin, scenario.Site, scenario.Posts, id, 1, ItemsApi.Revision(item)));

        Assert.Equal(4, rolledBack.GetProperty("liveVersion").GetInt32());
        Assert.Equal("zz-canary-v1", ItemsApi.Values(rolledBack, "published")["f1"]!.GetValue<string>());
        Assert.Equal("zz-canary-v3", ItemsApi.Values(rolledBack)["f1"]!.GetValue<string>());
        Assert.Equal("changed", rolledBack.GetProperty("status").GetString());

        var history = await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, id));
        var newest = history.GetProperty("versions")[0];
        Assert.Equal((4, "rollback", 1, true), (newest.GetProperty("number").GetInt32(), newest.GetProperty("kind").GetString(), newest.GetProperty("restoredFrom").GetInt32(), newest.GetProperty("live").GetBoolean()));
        Assert.Equal("rolledBack", history.GetProperty("events")[0].GetProperty("kind").GetString());
        Assert.DoesNotContain(PagesApi.CanaryMarker, history.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_version_that_fails_todays_rules_is_refused_but_can_be_copied_into_the_draft()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-rollback-refused");
        var ann = await scenario.PersonAsync("Ann");
        var item = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-v1", ["f9"] = ItemsApi.Id(ann).ToString() });
        var id = ItemsApi.Id(item);
        item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        item = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-v2" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)));
        item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));

        // Version 1 named Ann; Ann is deleted (nothing refers to her any more but version 1).
        Assert.Equal(HttpStatusCode.NoContent, (await ItemsApi.DeleteAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann), ItemsApi.Revision(ann))).StatusCode);

        var refused = await PagesApi.ProblemAsync(await ItemsApi.RollbackAsync(scenario.Admin, scenario.Site, scenario.Posts, id, 1, ItemsApi.Revision(item)));
        Assert.Equal("cms.version_not_publishable", refused.Code);
        Assert.Equal([("f9", "reference_missing")], ItemsApi.Problems(refused.Body.GetProperty("report")));

        var copied = await PagesApi.OkAsync(await ItemsApi.CopyToDraftAsync(scenario.Admin, scenario.Site, scenario.Posts, id, 1, ItemsApi.Revision(item)));
        Assert.Equal("zz-canary-v1", ItemsApi.Values(copied)["f1"]!.GetValue<string>());
        Assert.Equal(2, copied.GetProperty("liveVersion").GetInt32());
        Assert.Contains(("f9", "reference_missing"), ItemsApi.Problems(copied.GetProperty("report")));
    }

    [Fact]
    public async Task A_version_whose_slug_another_item_now_holds_is_refused()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-rollback-slug");
        var item = await scenario.PostAsync("original");
        var id = ItemsApi.Id(item);
        item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        item = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("renamed", new JsonObject { ["f1"] = "zz-canary-r" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)));
        item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        await scenario.PostAsync("original");

        var refused = await PagesApi.ProblemAsync(await ItemsApi.RollbackAsync(scenario.Admin, scenario.Site, scenario.Posts, id, 1, ItemsApi.Revision(item)));

        Assert.Equal("cms.version_not_publishable", refused.Code);
        Assert.Equal([("slug", "invalid_slug")], ItemsApi.Problems(refused.Body.GetProperty("report")));
        Assert.Equal("cms.version_not_found", (await PagesApi.ProblemAsync(await ItemsApi.RollbackAsync(scenario.Admin, scenario.Site, scenario.Posts, id, 9, ItemsApi.Revision(item)))).Code);
    }
}
