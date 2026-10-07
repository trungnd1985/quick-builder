using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// Schema changes reach every stored copy of an item (015 research IT-11, IT-12; 014 FR-024):
/// drafts and published values, kept versions without fill-ins, and what is derived.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemSchemaMigrationTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_removed_field_leaves_every_version_and_a_fill_in_never_reaches_one()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-migrate-versions");
        var item = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-1", ["f3"] = 7 });
        var id = ItemsApi.Id(item);
        item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));

        var posts = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts);
        var schema = CmsApi.Schema(posts);
        var fields = CmsApi.Fields(schema);
        fields.Remove(fields.Single(f => f!["key"]!.GetValue<string>() == "reading-time"));
        fields.Add(CmsApi.Field("~1", "subtitle", "text", settings: new { multiline = false, maxLength = 100 }));
        await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.Site, posts, schema, new Dictionary<string, object> { ["~1"] = "zz-canary-filled" });

        var versions = await VersionsAsync(scenario, id);
        Assert.Equal(2, versions.Count);
        Assert.All(versions, v => Assert.DoesNotContain("\"f3\"", v, StringComparison.Ordinal));
        Assert.All(versions, v => Assert.DoesNotContain("zz-canary-filled", v, StringComparison.Ordinal));

        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal("zz-canary-filled", ItemsApi.Values(after)["f11"]!.GetValue<string>());
        Assert.Equal("zz-canary-filled", ItemsApi.Values(after, "published")["f11"]!.GetValue<string>());
        Assert.True(ItemsApi.Revision(after) > ItemsApi.Revision(item));
    }

    [Fact]
    public async Task A_lossy_conversion_converts_versions_and_a_rename_writes_none()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-migrate-convert");
        var item = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-1", ["f10"] = new JsonArray(ItemsApi.Id(await scenario.PersonAsync("Ann")).ToString()) });
        var id = ItemsApi.Id(item);
        await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));

        var posts = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts);
        var renamed = CmsApi.Schema(posts);
        CmsApi.Fields(renamed).Single(f => f!["key"]!.GetValue<string>() == "related")!["name"] = "See also";
        posts = await CmsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, CmsApi.SaveBody(posts, renamed));
        var before = await VersionsAsync(scenario, id);

        // Multi-reference to reference is lossy: the first item is kept, in the version too.
        var converted = CmsApi.Schema(posts);
        CmsApi.Fields(converted).Single(f => f!["key"]!.GetValue<string>() == "related")!["type"] = "reference";
        await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.Site, posts, converted);

        var after = Assert.Single(await VersionsAsync(scenario, id));
        Assert.Contains("\"f10\":[", Assert.Single(before).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("\"f10\":[", after.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Names_and_attention_follow_a_new_title_field_and_a_newly_required_field()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-migrate-index");
        var item = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-title" });
        var id = ItemsApi.Id(item);

        var posts = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts);
        var schema = CmsApi.Schema(posts);
        CmsApi.Fields(schema).Single(f => f!["key"]!.GetValue<string>() == "reading-time")!["required"] = true;
        await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.Site, posts, schema);

        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.True(after.GetProperty("attention").GetBoolean());
        Assert.Contains(("f3", "missing_required"), ItemsApi.Problems(after.GetProperty("report")));
        Assert.Equal("zz-canary-title", after.GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_interrupted_change_leaves_items_versions_and_derived_rows_as_they_were()
    {
        using var observer = new TestMigrationObserver();
        using var cms = await CmsScenario.CreateAsync(database, "items-migrate-interrupted", TestMigrationObserver.Install(observer));
        var people = await cms.CollectionAsync("people", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "name", "text", settings: new { multiline = false, maxLength = 100 })),
        });
        var posts = await cms.CollectionAsync("posts", CmsApi.EveryType(people.GetProperty("id").GetGuid()));
        var postsId = posts.GetProperty("id").GetGuid();
        await ItemSeed.AddAsync(cms, cms.SiteA, postsId, 600, published: true, index: true);

        var before = await SnapshotAsync(cms, postsId);
        var removed = CmsApi.Schema(posts);
        CmsApi.Fields(removed).RemoveAt(2);
        var preview = await CmsApi.PreviewOkAsync(cms.Admin, cms.SiteA, postsId, CmsApi.SaveBody(posts, removed));
        observer.FailAfterBatch = 1;

        var response = await CmsApi.SaveAsync(cms.Admin, cms.SiteA, postsId, CmsApi.SaveBody(posts, removed, digest: preview.GetProperty("digest").GetString()));

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(before, await SnapshotAsync(cms, postsId));
    }

    private static Task<List<string>> VersionsAsync(ItemsScenario scenario, Guid itemId) =>
        scenario.Cms.WithDatabaseAsync(async cms => (await cms.ForSite<ItemVersion>(scenario.Site).AsNoTracking()
                .Where(v => v.ItemId == itemId).OrderBy(v => v.Number).ToListAsync())
            .Select(v => v.Values.RootElement.GetRawText())
            .ToList());

    /// <summary>Every stored copy and derived row of a collection's items, as text.</summary>
    private static Task<string> SnapshotAsync(CmsScenario scenario, Guid collectionId) =>
        scenario.WithDatabaseAsync(async cms =>
        {
            var items = await cms.ForSite<Item>(scenario.SiteA).AsNoTracking().Where(i => i.CollectionId == collectionId).OrderBy(i => i.Id).ToListAsync();
            var ids = items.Select(i => i.Id).ToList();
            var versions = await cms.ForSite<ItemVersion>(scenario.SiteA).AsNoTracking().Where(v => ids.Contains(v.ItemId)).OrderBy(v => v.ItemId).ThenBy(v => v.Number).ToListAsync();
            var media = await cms.ForSite<ItemMediaUse>(scenario.SiteA).AsNoTracking().Where(m => ids.Contains(m.ItemId)).OrderBy(m => m.ItemId).ToListAsync();
            return string.Join('\n', items.Select(i => $"{i.Id}|{i.Revision}|{i.Draft.RootElement.GetRawText()}|{i.Published?.RootElement.GetRawText()}|{i.Attention?.RootElement.GetRawText()}"))
                + string.Join('\n', versions.Select(v => $"{v.ItemId}|{v.Number}|{v.Values.RootElement.GetRawText()}"))
                + string.Join('\n', media.Select(m => $"{m.ItemId}|{m.MediaId}|{m.InDraft}|{m.InLive}"));
        });
}
