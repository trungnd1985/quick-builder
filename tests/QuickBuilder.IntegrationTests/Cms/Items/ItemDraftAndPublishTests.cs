using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>Draft and published data are separate worlds (015 US2, FR-019 to FR-023, SC-004; Principle VII).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemDraftAndPublishTests(DatabaseFixture database)
{
    [Fact]
    public async Task Publishing_makes_version_1_live_and_later_draft_writes_never_touch_published_data()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-publish");
        var created = await scenario.PostAsync("hello");
        var id = ItemsApi.Id(created);

        var published = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(created));
        Assert.Equal("published", published.GetProperty("status").GetString());
        Assert.Equal(1, published.GetProperty("liveVersion").GetInt32());
        Assert.Equal("hello", published.GetProperty("publishedSlug").GetString());
        Assert.NotEqual(JsonValueKind.Null, published.GetProperty("publishedAtUtc").ValueKind);
        var live = published.GetProperty("published").GetRawText();

        // Save, copy a version into the draft, duplicate: the published data stays byte for byte.
        var saved = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("hello", new JsonObject { ["f1"] = "zz-canary-changed" }, scenario.PostsSchemaRevision, ItemsApi.Revision(published)));
        Assert.Equal("changed", saved.GetProperty("status").GetString());
        Assert.Equal(live, saved.GetProperty("published").GetRawText());

        var copied = await PagesApi.OkAsync(await ItemsApi.CopyToDraftAsync(scenario.Admin, scenario.Site, scenario.Posts, id, 1, ItemsApi.Revision(saved)));
        Assert.Equal(live, copied.GetProperty("published").GetRawText());
        Assert.Equal("published", copied.GetProperty("status").GetString());

        await PagesApi.OkAsync(await ItemsApi.PostAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "duplicate"), HttpStatusCode.Created);
        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal(live, after.GetProperty("published").GetRawText());

        var events = (await scenario.Cms.OutboxAsync()).Where(e => e.Type == "Cms.ItemPublished").ToList();
        var payload = JsonDocument.Parse(Assert.Single(events).Payload).RootElement;
        Assert.Equal(1, payload.GetProperty("VersionNumber").GetInt32());
        Assert.Equal("publish", payload.GetProperty("Kind").GetString());
        Assert.False(payload.GetProperty("SlugChanged").GetBoolean());
        Assert.DoesNotContain(PagesApi.CanaryMarker, events[0].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_draft_that_needs_attention_is_not_published_and_every_field_is_listed()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-publish-attention");
        var created = await scenario.PostAsync("incomplete", new JsonObject { ["f3"] = -1 });

        var refused = await PagesApi.ProblemAsync(await ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(created), ItemsApi.Revision(created)));

        Assert.Equal((HttpStatusCode.Conflict, "cms.item_needs_attention"), (refused.Status, refused.Code));
        Assert.Equal([("f1", "missing_required"), ("f3", "out_of_range")], ItemsApi.Problems(refused.Body.GetProperty("report")));
    }

    [Fact]
    public async Task References_to_unpublished_items_are_allowed_and_listed()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-publish-references");
        var ann = await scenario.PersonAsync("Ann");
        var post = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-post", ["f9"] = ItemsApi.Id(ann).ToString() });

        var published = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post));

        var reference = Assert.Single(published.GetProperty("unpublishedReferences").EnumerateArray());
        Assert.Equal(ItemsApi.Id(ann), reference.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Unpublishing_keeps_the_draft_and_versions_and_counts_published_referrers()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-unpublish");
        var ann = await scenario.PersonAsync("Ann");
        ann = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann), ItemsApi.Revision(ann));
        foreach (var slug in new[] { "one", "two" })
        {
            var post = await scenario.PostAsync(slug, new JsonObject { ["f1"] = $"zz-canary-{slug}", ["f9"] = ItemsApi.Id(ann).ToString() });
            if (slug == "one")
            {
                await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post));
            }
        }

        // Referrers: two in drafts, one of them published too.
        var all = await PagesApi.OkAsync(await ItemsApi.ReferrersAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann)));
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        var first = all.GetProperty("referrers")[0];
        Assert.Equal("author", first.GetProperty("fieldName").GetString());
        var publishedOnly = await PagesApi.OkAsync(await ItemsApi.ReferrersAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann), publishedOnly: true));
        Assert.Equal(1, publishedOnly.GetProperty("total").GetInt32());

        var unpublished = await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann), ItemsApi.Revision(ann)));
        Assert.Equal("draft", unpublished.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unpublished.GetProperty("published").ValueKind);
        Assert.Equal(JsonValueKind.Null, unpublished.GetProperty("liveVersion").ValueKind);
        Assert.True(JsonNode.DeepEquals(ItemsApi.Values(ann), ItemsApi.Values(unpublished)));

        var history = await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann)));
        Assert.Equal(1, history.GetProperty("versions").GetArrayLength());
        Assert.Equal("unpublished", history.GetProperty("events")[0].GetProperty("kind").GetString());

        var again = await PagesApi.ProblemAsync(await ItemsApi.UnpublishAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann), ItemsApi.Revision(unpublished)));
        Assert.Equal("cms.item_not_published", again.Code);
    }

    [Fact]
    public async Task Fifty_versions_are_kept_including_the_live_one()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-retention");
        var item = await scenario.PostAsync("kept");
        var id = ItemsApi.Id(item);

        for (var n = 1; n <= 52; n++)
        {
            item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        }

        var history = await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, id));
        var numbers = history.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()).ToList();
        Assert.Equal(Enumerable.Range(3, 50).Reverse(), numbers);
        Assert.True(history.GetProperty("versions")[0].GetProperty("live").GetBoolean());
    }
}
