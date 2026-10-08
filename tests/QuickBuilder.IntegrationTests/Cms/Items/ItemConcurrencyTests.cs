using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>Concurrent writes to items (015 FR-005, US2 scenario 7; research IT-03, IT-08).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemConcurrencyTests(DatabaseFixture database)
{
    [Fact]
    public async Task Of_two_concurrent_publishes_one_wins_and_the_retry_makes_version_2()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-concurrent-publish");
        var item = await scenario.PostAsync("race");
        var id = ItemsApi.Id(item);

        var responses = await Task.WhenAll(
            ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item)),
            ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = await PagesApi.ProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK));
        Assert.Equal("cms.item_conflict", loser.Code);

        var retried = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, int.Parse(loser.Text("currentRevision")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2, retried.GetProperty("liveVersion").GetInt32());
        var history = await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, id));
        Assert.Equal([2, 1], history.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
    }

    [Fact]
    public async Task A_save_racing_a_publish_gives_one_conflict()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-save-publish");
        var item = await scenario.PostAsync("race");
        var id = ItemsApi.Id(item);

        var responses = await Task.WhenAll(
            ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item)),
            ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
                ItemsApi.Draft("race", new JsonObject { ["f1"] = "zz-canary-saved" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal("cms.item_conflict", (await PagesApi.ProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK))).Code);
    }

    [Fact]
    public async Task Two_items_racing_for_one_slug_get_exactly_one_refusal()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-slug-race");

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(n => ItemsApi.CreateAsync(scenario.Admin, scenario.Site, scenario.Posts,
            ItemsApi.Draft("same", new JsonObject { ["f1"] = $"zz-canary-{n}" }, scenario.PostsSchemaRevision))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var refused = await PagesApi.ProblemAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created));
        Assert.Equal(("cms.item_slug_taken", "same-2"), (refused.Code, refused.Text("suggestion")));
    }

    [Fact]
    public async Task A_slug_held_as_another_items_published_slug_cannot_be_taken()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-published-slug");
        var first = await scenario.PostAsync("original");
        first = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(first), ItemsApi.Revision(first));

        // The first item's draft moves to another slug; its published slug still holds "original".
        await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(first),
            ItemsApi.Draft("renamed", new JsonObject { ["f1"] = "zz-canary-first" }, scenario.PostsSchemaRevision, ItemsApi.Revision(first)));

        var refused = await PagesApi.ProblemAsync(await ItemsApi.CreateAsync(scenario.Admin, scenario.Site, scenario.Posts,
            ItemsApi.Draft("original", new JsonObject { ["f1"] = "zz-canary-second" }, scenario.PostsSchemaRevision)));
        Assert.Equal("cms.item_slug_taken", refused.Code);
    }
}
