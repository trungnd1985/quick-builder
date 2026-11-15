using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>An item that others refer to cannot be deleted (015 US5, FR-008, SC-005; clarification Q3, research IT-10).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemReferenceIntegrityTests(DatabaseFixture database)
{
    [Fact]
    public async Task Deleting_a_referenced_item_is_refused_listing_the_referrers_from_drafts_and_published_values()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-delete-referenced");
        var ann = await scenario.PersonAsync("Ann");
        var annId = ItemsApi.Id(ann);

        var draftOnly = await scenario.PostAsync("draft-only", new JsonObject { ["f1"] = "zz-canary-d", ["f9"] = annId.ToString() });
        var publishedOnly = await scenario.PostAsync("published-only", new JsonObject { ["f1"] = "zz-canary-p", ["f9"] = annId.ToString() });
        publishedOnly = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(publishedOnly), ItemsApi.Revision(publishedOnly));
        await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(publishedOnly),
            ItemsApi.Draft("published-only", new JsonObject { ["f1"] = "zz-canary-p" }, scenario.PostsSchemaRevision, ItemsApi.Revision(publishedOnly)));

        var refused = await PagesApi.ProblemAsync(await ItemsApi.DeleteAsync(scenario.Admin, scenario.Site, scenario.People, annId, ItemsApi.Revision(ann)));

        Assert.Equal((HttpStatusCode.Conflict, "cms.item_referenced", "2"), (refused.Status, refused.Code, refused.Text("total")));
        var referrers = refused.Body.GetProperty("referrers").EnumerateArray()
            .ToDictionary(r => r.GetProperty("itemId").GetGuid(), r => (r.GetProperty("inDraft").GetBoolean(), r.GetProperty("inPublished").GetBoolean(), r.GetProperty("collectionName").GetString()));
        Assert.Equal((true, false), (referrers[ItemsApi.Id(draftOnly)].Item1, referrers[ItemsApi.Id(draftOnly)].Item2));
        Assert.Equal((false, true), (referrers[ItemsApi.Id(publishedOnly)].Item1, referrers[ItemsApi.Id(publishedOnly)].Item2));

        // Remove the references; the deletion goes through and frees the slug.
        await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(draftOnly),
            ItemsApi.Draft("draft-only", new JsonObject { ["f1"] = "zz-canary-d" }, scenario.PostsSchemaRevision, ItemsApi.Revision(draftOnly)));
        var current = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(publishedOnly));
        await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(publishedOnly), ItemsApi.Revision(current)));

        Assert.Equal(HttpStatusCode.OK, (await ItemsApi.DeleteAsync(scenario.Admin, scenario.Site, scenario.People, annId, ItemsApi.Revision(ann))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ItemsApi.GetAsync(scenario.Admin, scenario.Site, scenario.People, annId)).StatusCode);
        await scenario.PersonAsync("Ann");

        var deleted = (await scenario.Cms.OutboxAsync()).Single(e => e.Type == "Cms.ItemDeleted");
        Assert.False(JsonDocument.Parse(deleted.Payload).RootElement.GetProperty("WasPublished").GetBoolean());
    }

    [Fact]
    public async Task Up_to_50_referrers_are_named_with_the_total()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-many-referrers");
        var ann = await scenario.PersonAsync("Ann");
        for (var n = 0; n < 55; n++)
        {
            await scenario.PostAsync($"post-{n}", new JsonObject { ["f1"] = $"zz-canary-{n}", ["f10"] = new JsonArray(ItemsApi.Id(ann).ToString()) });
        }

        var refused = await PagesApi.ProblemAsync(await ItemsApi.DeleteAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann), ItemsApi.Revision(ann)));

        Assert.Equal("55", refused.Text("total"));
        Assert.Equal(50, refused.Body.GetProperty("referrers").GetArrayLength());
    }

    [Fact]
    public async Task A_reference_added_at_the_same_moment_is_caught_by_the_database()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-reference-race");
        var ann = await scenario.PersonAsync("Ann");
        var post = await scenario.PostAsync("post");

        // The reference row is written straight into the table, as a concurrent save would have
        // committed it after the delete read its referrers: the foreign key still refuses.
        await scenario.Cms.WithDatabaseAsync(async cms =>
        {
            cms.ItemReferences.Add(new ItemReference(scenario.Site, ItemsApi.Id(post), "f9", ItemsApi.Id(ann), true, false, false));
            return await cms.SaveChangesAsync();
        });

        var refused = await PagesApi.ProblemAsync(await ItemsApi.DeleteAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(ann), ItemsApi.Revision(ann)));
        Assert.Equal("cms.item_referenced", refused.Code);
    }

    [Fact]
    public async Task Deleting_a_whole_collection_whose_items_refer_to_each_other_still_works()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-collection-delete");

        // A collection whose items refer to each other: "related" targets the collection itself.
        var notes = await scenario.Cms.CollectionAsync("notes", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 100 })),
        });
        var notesId = notes.GetProperty("id").GetGuid();
        var schema = CmsApi.Schema(notes);
        CmsApi.Fields(schema).Add(CmsApi.Field("~2", "related", "multiReference", settings: new { target = notesId }));
        notes = await CmsApi.SaveOkAsync(scenario.Admin, scenario.Site, notesId, CmsApi.SaveBody(notes, schema));
        var revision = notes.GetProperty("schemaRevision").GetInt32();

        var first = await ItemsApi.CreateOkAsync(scenario.Admin, scenario.Site, notesId, ItemsApi.Draft("first", new JsonObject { ["f1"] = "zz-canary-1" }, revision));
        var second = await ItemsApi.CreateOkAsync(scenario.Admin, scenario.Site, notesId,
            ItemsApi.Draft("second", new JsonObject { ["f1"] = "zz-canary-2", ["f2"] = new JsonArray(ItemsApi.Id(first).ToString()) }, revision));
        await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, notesId, ItemsApi.Id(first),
            ItemsApi.Draft("first", new JsonObject { ["f1"] = "zz-canary-1", ["f2"] = new JsonArray(ItemsApi.Id(second).ToString()) }, revision, ItemsApi.Revision(first)));
        Assert.Equal(2, await scenario.Cms.WithDatabaseAsync(cms => cms.ForSite<ItemReference>(scenario.Site).CountAsync()));

        var current = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, notesId);
        var deleted = await CmsApi.DeleteAsync(scenario.Admin, scenario.Site, notesId, current.GetProperty("revision").GetInt32());
        Assert.True(deleted.StatusCode == HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        Assert.Equal(0, await scenario.Cms.WithDatabaseAsync(cms => cms.ForSite<ItemReference>(scenario.Site).CountAsync()));
    }

    [Fact]
    public async Task No_reference_points_at_a_missing_item_after_a_mixed_sequence_of_writes()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-no-dangling");
        var people = new List<JsonElement>();
        for (var n = 0; n < 4; n++)
        {
            people.Add(await scenario.PersonAsync($"P{n}"));
        }

        var random = new Random(15);
        for (var n = 0; n < 20 && people.Count > 0; n++)
        {
            var target = people[random.Next(people.Count)];
            var post = await scenario.PostAsync($"post-{n}", new JsonObject { ["f1"] = $"zz-canary-{n}", ["f9"] = ItemsApi.Id(target).ToString() });
            if (random.Next(2) == 0)
            {
                post = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(post), ItemsApi.Revision(post));
            }

            if (random.Next(3) == 0)
            {
                var victim = people[random.Next(people.Count)];
                var current = await ItemsApi.GetAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(victim));
                if (current.StatusCode == HttpStatusCode.OK
                    && (await ItemsApi.DeleteAsync(scenario.Admin, scenario.Site, scenario.People, ItemsApi.Id(victim), ItemsApi.Revision(await PagesApi.OkAsync(current)))).StatusCode == HttpStatusCode.OK)
                {
                    people.Remove(victim);
                }
            }
        }

        var dangling = await scenario.Cms.WithDatabaseAsync(async cms =>
        {
            var items = cms.ForSite<Item>(scenario.Site).Select(i => i.Id);
            return await cms.ForSite<ItemReference>(scenario.Site).CountAsync(r => !items.Contains(r.TargetItemId));
        });
        Assert.Equal(0, dangling);
    }

    [Fact]
    public async Task A_duplicate_is_a_fresh_draft_with_a_copy_name_and_slug()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-duplicate");
        var original = await scenario.PostAsync("linen-shirt", new JsonObject { ["f1"] = "zz-canary-Linen shirt", ["f3"] = 4 });
        original = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(original), ItemsApi.Revision(original));

        var copy = await PagesApi.OkAsync(await ItemsApi.PostAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(original), "duplicate"), HttpStatusCode.Created);
        var second = await PagesApi.OkAsync(await ItemsApi.PostAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(original), "duplicate"), HttpStatusCode.Created);

        Assert.Equal(("linen-shirt-copy", "linen-shirt-copy-2"), (copy.GetProperty("slug").GetString(), second.GetProperty("slug").GetString()));
        Assert.Equal("zz-canary-Linen shirt (copy)", copy.GetProperty("name").GetString());
        Assert.Equal(4, ItemsApi.Values(copy)["f3"]!.GetValue<int>());
        Assert.Equal("draft", copy.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, copy.GetProperty("published").ValueKind);
        var history = await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(copy)));
        Assert.Equal(0, history.GetProperty("versions").GetArrayLength());
    }
}
