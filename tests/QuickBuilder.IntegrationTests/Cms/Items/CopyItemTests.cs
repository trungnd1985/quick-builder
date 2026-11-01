using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// "Save my version as a copy" for items (023 US4, FR-020 to FR-022, research CE-11): a new
/// unpublished item of the same collection that keeps every value the fields can still hold.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CopyItemTests(DatabaseFixture database)
{
    private static readonly DateTimeOffset LoadedAt = new(2026, 10, 25, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_copy_holds_the_members_version_unpublished_with_a_free_slug_and_its_origin()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "copy-item");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);
        await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));

        var first = await CopyOkAsync(scenario, id, "post", new JsonObject { ["f1"] = "zz-canary-mine", ["f3"] = 12 });
        var second = await CopyOkAsync(scenario, id, "post", new JsonObject { ["f1"] = "zz-canary-mine-again" });

        Assert.Equal("post-copy", first.GetProperty("slug").GetString());
        Assert.Equal("post-copy-2", second.GetProperty("slug").GetString());
        Assert.Equal("draft", first.GetProperty("status").GetString());
        Assert.Equal("zz-canary-mine", ItemsApi.Values(first)["f1"]!.GetValue<string>());
        Assert.Equal(12, ItemsApi.Values(first)["f3"]!.GetValue<int>());
        Assert.Empty(first.GetProperty("droppedFields").EnumerateArray());

        var origin = first.GetProperty("copyOrigin");
        Assert.Equal(id, origin.GetProperty("sourceId").GetGuid());
        Assert.Equal(LoadedAt, origin.GetProperty("sourceSavedAtUtc").GetDateTimeOffset());

        var history = await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(first)));
        Assert.Equal("createdAsCopy", Assert.Single(history.GetProperty("events").EnumerateArray()).GetProperty("kind").GetString());

        // The original is untouched, and still published.
        var original = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal("published", original.GetProperty("status").GetString());
        Assert.NotEqual("zz-canary-mine", ItemsApi.Values(original)["f1"]!.GetValue<string>());
    }

    [Fact]
    public async Task Values_the_fields_can_no_longer_hold_are_left_out_and_named()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "copy-item-dropped");
        var item = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-1", ["f3"] = 7 });
        var id = ItemsApi.Id(item);

        // Since the form was built, a field went, and the member typed text into a number field.
        var copy = await CopyOkAsync(scenario, id, "post", new JsonObject
        {
            ["f1"] = "zz-canary-mine",
            ["f3"] = "not a number",
            ["f999"] = "from a removed field",
        });

        Assert.Equal(["f3", "f999"], copy.GetProperty("droppedFields").EnumerateArray().Select(f => f.GetString()).Order(StringComparer.Ordinal));
        var values = ItemsApi.Values(copy);
        Assert.Equal("zz-canary-mine", values["f1"]!.GetValue<string>());
        Assert.False(values.ContainsKey("f3"));
        Assert.False(values.ContainsKey("f999"));
    }

    [Fact]
    public async Task Another_sites_item_is_not_found()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "copy-item-site");
        var item = await scenario.PostAsync("post");

        var response = await scenario.Admin.PostAsJsonAsync(
            new Uri($"{ItemsApi.ItemPath(scenario.Cms.SiteB, scenario.Posts, ItemsApi.Id(item))}/copies", UriKind.Relative),
            Body("post", new JsonObject { ["f1"] = "x" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static object Body(string slug, JsonObject values) =>
        new { slug, values = new JsonObject { ["schemaVersion"] = 1, ["values"] = values }, baseSavedAtUtc = LoadedAt };

    private static async Task<System.Text.Json.JsonElement> CopyOkAsync(ItemsScenario scenario, Guid itemId, string slug, JsonObject values) =>
        await PagesApi.OkAsync(
            await scenario.Admin.PostAsJsonAsync(new Uri($"{ItemsApi.ItemPath(scenario.Site, scenario.Posts, itemId)}/copies", UriKind.Relative), Body(slug, values)),
            HttpStatusCode.Created);
}
