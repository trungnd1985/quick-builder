using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// Item draft saves compare the draft revision (023 research CE-08): publishing, unpublishing
/// and schedules by someone else no longer refuse them, while a change to the draft still does,
/// naming who made it (CE-09).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemPublishDoesNotConflictTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_publish_unpublish_or_schedule_by_someone_else_does_not_refuse_the_next_draft_save()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-publish-no-conflict");
        var (ben, _) = await MemberAsync(scenario, "Ben Hale");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);
        var draftRevision = ItemsApi.DraftRevision(item);

        var published = await ItemsApi.PublishOkAsync(ben, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(ben, scenario.Site, scenario.Posts, id, ItemsApi.Revision(published)));
        var current = await ItemsApi.GetOkAsync(ben, scenario.Site, scenario.Posts, id);
        await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(ben, scenario.Site, scenario.Posts, id, "publish", DateTimeOffset.UtcNow.AddDays(2), ItemsApi.Revision(current)));

        var saved = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.DraftOf("post", new JsonObject { ["f1"] = "zz-canary-admin" }, scenario.PostsSchemaRevision, draftRevision));

        Assert.Equal(draftRevision + 1, ItemsApi.DraftRevision(saved));
        Assert.Equal("zz-canary-admin", ItemsApi.Values(saved)["f1"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_save_on_an_older_draft_names_the_member_who_changed_it()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-draft-conflict");
        var (ben, benId) = await MemberAsync(scenario, "Ben Hale");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);
        var loaded = ItemsApi.DraftRevision(item);

        var bens = await ItemsApi.SaveOkAsync(ben, scenario.Site, scenario.Posts, id,
            ItemsApi.DraftOf("post", new JsonObject { ["f1"] = "zz-canary-ben" }, scenario.PostsSchemaRevision, loaded));
        var refused = await PagesApi.RefusedAsync(
            await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
                ItemsApi.DraftOf("post", new JsonObject { ["f1"] = "zz-canary-admin" }, scenario.PostsSchemaRevision, loaded)),
            HttpStatusCode.Conflict,
            "cms.item_conflict");

        Assert.Equal("member", refused.Text("changeKind"));
        Assert.Equal(benId, refused.Body.GetProperty("changedBy").GetProperty("id").GetGuid());
        Assert.Equal("Ben Hale", refused.Body.GetProperty("changedBy").GetProperty("displayName").GetString());
        Assert.True(refused.Body.TryGetProperty("changedAtUtc", out _));
        Assert.Equal(ItemsApi.DraftRevision(bens), refused.Body.GetProperty("currentDraftRevision").GetInt32());
        Assert.Equal("zz-canary-ben", ItemsApi.Values(await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id))["f1"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_save_without_a_draft_revision_is_checked_on_the_item_revision_as_before_023()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-old-client");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);
        await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));

        await PagesApi.RefusedAsync(
            await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
                ItemsApi.Draft("post", new JsonObject { ["f1"] = "zz-canary-old" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item))),
            HttpStatusCode.Conflict,
            "cms.item_conflict");
    }

    [Fact]
    public async Task Copying_a_version_into_the_draft_moves_the_draft_revision()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-copy-moves");
        var item = await scenario.PostAsync("post");
        var id = ItemsApi.Id(item);
        var loaded = ItemsApi.DraftRevision(item);
        var published = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));

        var copied = await PagesApi.OkAsync(await ItemsApi.CopyToDraftAsync(scenario.Admin, scenario.Site, scenario.Posts, id, 1, ItemsApi.Revision(published)));

        Assert.Equal(loaded + 1, ItemsApi.DraftRevision(copied));
        await PagesApi.RefusedAsync(
            await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
                ItemsApi.DraftOf("post", new JsonObject { ["f1"] = "zz-canary-late" }, scenario.PostsSchemaRevision, loaded)),
            HttpStatusCode.Conflict,
            "cms.item_conflict");
    }

    [Fact]
    public async Task A_schema_change_that_rewrites_the_draft_is_named_as_a_system_change()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schema-conflict");
        var item = await scenario.PostAsync("post", new JsonObject { ["f1"] = "zz-canary-1", ["f3"] = 7 });
        var id = ItemsApi.Id(item);
        var loaded = ItemsApi.DraftRevision(item);

        var posts = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts);
        var schema = CmsApi.Schema(posts);
        var fields = CmsApi.Fields(schema);
        fields.Remove(fields.Single(f => f!["key"]!.GetValue<string>() == "reading-time"));
        await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.Site, posts, schema);
        var schemaRevision = (await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts)).GetProperty("schemaRevision").GetInt32();

        // The admin rebuilds the form on the new schema, keeping the entered values (015).
        var refused = await PagesApi.RefusedAsync(
            await ItemsApi.SaveAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
                ItemsApi.DraftOf("post", new JsonObject { ["f1"] = "zz-canary-2" }, schemaRevision, loaded)),
            HttpStatusCode.Conflict,
            "cms.item_conflict");

        Assert.Equal("schema_change", refused.Text("changeKind"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, refused.Body.GetProperty("changedBy").ValueKind);
    }

    private static async Task<(HttpClient Client, Guid Id)> MemberAsync(ItemsScenario scenario, string name)
    {
        var user = await IdentitySeed.SeedUserAsync(scenario.Cms.App.Services, siteRoles: [(scenario.Site, SystemRole.Editor)], displayName: name);
        return (await IdentitySeed.CreateSignedInClientAsync(scenario.Cms.App, user.Id), user.Id);
    }
}
