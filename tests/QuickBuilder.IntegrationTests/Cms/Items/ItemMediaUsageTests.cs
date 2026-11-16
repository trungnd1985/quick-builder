using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>The media library counts items as places; a deleted file puts its items in attention (015 FR-018, research IT-11, IT-15).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemMediaUsageTests(DatabaseFixture database)
{
    private static string UsagePath(Guid siteId, Guid fileId) => $"/api/v1/media/sites/{siteId}/files/{fileId}/usage";

    [Fact]
    public async Task Usage_counts_and_lists_items_and_a_published_item_makes_the_file_live()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-media-usage");
        var draft = await scenario.PostAsync("draft", new JsonObject { ["f1"] = "zz-canary-draft", ["f6"] = new JsonObject { ["mediaId"] = scenario.Image.ToString() } });
        var live = await scenario.PostAsync("live", new JsonObject { ["f1"] = "zz-canary-live", ["f6"] = new JsonObject { ["mediaId"] = scenario.Image.ToString() } });
        await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(live), ItemsApi.Revision(live));

        var usage = await PagesApi.OkAsync(await scenario.Admin.GetAsync(new Uri(UsagePath(scenario.Site, scenario.Image), UriKind.Relative)));
        var summary = usage.GetProperty("summary");
        Assert.Equal((2, 2, 1, true), (summary.GetProperty("places").GetInt32(), summary.GetProperty("draftPlaces").GetInt32(), summary.GetProperty("livePlaces").GetInt32(), summary.GetProperty("inLiveUse").GetBoolean()));

        var places = usage.GetProperty("places").EnumerateArray().Where(p => p.GetProperty("kind").GetString() == "item").ToList();
        Assert.Equal(2, places.Count);
        Assert.All(places, p => Assert.Equal(scenario.Posts, p.GetProperty("collectionId").GetGuid()));
        Assert.Contains(places, p => p.GetProperty("id").GetGuid() == ItemsApi.Id(draft) && !p.GetProperty("live").GetBoolean());

        // An Editor may not delete even their own file once a published item shows it: the 013 rule, unchanged.
        using var editor = await scenario.Cms.UserAsync((scenario.Site, SystemRole.Editor));
        var own = await QuickBuilder.IntegrationTests.Media.MediaApi.UploadOkAsync(editor, scenario.Site);
        var shown = await ItemsApi.CreateOkAsync(editor, scenario.Site, scenario.Posts,
            ItemsApi.Draft("editors", new JsonObject { ["f1"] = "zz-canary-e", ["f6"] = new JsonObject { ["mediaId"] = own.Id.ToString() } }, scenario.PostsSchemaRevision));
        await ItemsApi.PublishOkAsync(editor, scenario.Site, scenario.Posts, ItemsApi.Id(shown), ItemsApi.Revision(shown));
        var refused = await PagesApi.ProblemAsync(await editor.DeleteAsync(new Uri($"/api/v1/media/sites/{scenario.Site}/files/{own.Id}?confirmInUse=true", UriKind.Relative)));
        Assert.Equal("media.in_live_use", refused.Code);
    }

    [Fact]
    public async Task Deleting_a_file_puts_the_items_that_use_it_in_attention_and_publishing_is_refused()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-media-deleted");
        var post = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-post", ["f6"] = new JsonObject { ["mediaId"] = scenario.Image.ToString() } });
        Assert.False(post.GetProperty("attention").GetBoolean());

        var inUse = await PagesApi.ProblemAsync(await scenario.Admin.DeleteAsync(new Uri($"/api/v1/media/sites/{scenario.Site}/files/{scenario.Image}", UriKind.Relative)));
        Assert.Equal("media.in_use", inUse.Code);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.DeleteAsync(new Uri($"/api/v1/media/sites/{scenario.Site}/files/{scenario.Image}?confirmInUse=true", UriKind.Relative))).StatusCode);

        // The outbox is not running in tests: deliver Media.FileDeleted to the Cms handler by hand.
        await scenario.Cms.App.DeliverAsync<QuickBuilder.Media.Contracts.Events.FileDeleted>(
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, scenario.Site, scenario.Image, null));

        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(post));
        Assert.True(after.GetProperty("attention").GetBoolean());
        Assert.Equal([("f6", "media_missing")], ItemsApi.Problems(after.GetProperty("report")));

        var refused = await PagesApi.ProblemAsync(await ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(after)));
        Assert.Equal("cms.item_needs_attention", refused.Code);
    }
}
