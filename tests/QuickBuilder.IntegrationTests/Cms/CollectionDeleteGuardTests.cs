using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// A collection that pages or components use cannot be deleted, and a schema change says how
/// many of them read each field it would break (016 FR-010, FR-011, research TB-13; tasks T082).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CollectionDeleteGuardTests(DatabaseFixture database)
{
    private static async Task<HttpResponseMessage> DeleteAsync(TemplatesWorld world, Guid collectionId)
    {
        var collection = await CmsApi.GetOkAsync(world.Admin, world.Site, collectionId);
        return await CmsApi.DeleteAsync(world.Admin, world.Site, collectionId, collection.GetProperty("revision").GetInt32());
    }

    private static string ListOf(TemplatesWorld world, Guid collectionId) =>
        TemplatesWorld.Document(TemplatesWorld.ListOf("l1", collectionId, null, TemplatesWorld.BoundHeading("t", """{"field":"f1"}""")));

    [Fact]
    public async Task A_collection_used_in_a_draft_a_live_version_or_a_schedule_is_kept_and_its_uses_are_named()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "delete-guard");
        // Posts refer to people, so they are deleted first; nothing refers to posts.
        var draftOnly = await world.PageAsync("draft-only");
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, draftOnly), ListOf(world, world.Posts));
        var liveOnly = await world.PageAsync("live-only");
        await world.PublishDocumentAsync(liveOnly, ListOf(world, world.Posts));
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, liveOnly), PublishingApi.HeadingDocument("Moved on"));
        var scheduledOnly = await world.PageAsync("scheduled-only");
        var scheduledPath = PagesApi.PagePath(world.Site, scheduledOnly);
        await PublishingApi.SaveAsync(world.Admin, scheduledPath, ListOf(world, world.Posts));
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(
            world.Admin, scheduledPath, "publish", DateTimeOffset.UtcNow.AddDays(1), await PublishingApi.DraftVersionAsync(world.Admin, scheduledPath)));
        await PublishingApi.SaveAsync(world.Admin, scheduledPath, PublishingApi.HeadingDocument("Moved on"));

        var refused = await PagesApi.RefusedAsync(await DeleteAsync(world, world.Posts), HttpStatusCode.Conflict, "cms.collection_used_by_pages");

        Assert.Equal(3, refused.Body.GetProperty("total").GetInt32());
        var states = refused.Body.GetProperty("uses").EnumerateArray()
            .ToDictionary(u => u.GetProperty("id").GetGuid(), u => string.Join(',', u.GetProperty("states").EnumerateArray().Select(s => s.GetString())));
        Assert.Equal("draft", states[draftOnly]);
        Assert.Equal("live", states[liveOnly]);
        Assert.Equal("scheduled", states[scheduledOnly]);
        Assert.All(refused.Body.GetProperty("uses").EnumerateArray(), u => Assert.Equal("page", u.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task A_collection_is_deleted_once_no_page_uses_it()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "delete-after");
        var page = await world.PageAsync("news");
        var path = PagesApi.PagePath(world.Site, page);
        await PublishingApi.SaveAsync(world.Admin, path, ListOf(world, world.Posts));
        await PagesApi.RefusedAsync(await DeleteAsync(world, world.Posts), HttpStatusCode.Conflict, "cms.collection_used_by_pages");

        await PublishingApi.SaveAsync(world.Admin, path, PublishingApi.HeadingDocument("No list"));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(world, world.Posts)).StatusCode);
    }

    [Fact]
    public async Task A_reference_from_another_collection_is_refused_first_unchanged()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "delete-referenced");
        var page = await world.PageAsync("team");
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, page), ListOf(world, world.People));

        // Posts refer to people (014): that refusal comes first, as before.
        await PagesApi.RefusedAsync(await DeleteAsync(world, world.People), HttpStatusCode.Conflict, "cms.collection_referenced");
    }

    [Fact]
    public async Task A_schema_change_preview_counts_the_pages_reading_each_field_it_removes_or_retypes()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "bound-pages");
        var reading = await world.PageAsync("reading");
        await world.PublishDocumentAsync(reading, ListOf(world, world.Posts));
        var other = await world.PageAsync("other");
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, other), ListOf(world, world.Posts));

        var collection = await CmsApi.GetOkAsync(world.Admin, world.Site, world.Posts);
        var schema = CmsApi.Schema(collection);
        var fields = CmsApi.Fields(schema);
        var title = fields.First(f => f!["id"]!.GetValue<string>() == "f1")!;
        var readingTime = fields.First(f => f!["id"]!.GetValue<string>() == "f3")!;
        fields.Remove(title);
        readingTime["type"] = "text";
        readingTime.AsObject().Remove("decimals");
        readingTime.AsObject().Remove("min");
        readingTime["multiline"] = false;
        readingTime["maxLength"] = 50;
        if (schema["titleFieldId"] is not null)
        {
            schema.Remove("titleFieldId");
        }

        var preview = await CmsApi.PreviewOkAsync(world.Admin, world.Site, world.Posts, CmsApi.SaveBody(collection, schema));
        var bound = preview.GetProperty("boundPages");

        Assert.Equal(2, bound.GetProperty("f1").GetInt32());
        Assert.Equal(0, bound.GetProperty("f3").GetInt32());

        // A change to the details alone counts nothing.
        var renamed = await CmsApi.PreviewOkAsync(world.Admin, world.Site, world.Posts, CmsApi.SaveBody(collection, CmsApi.Schema(collection), name: "Articles"));
        Assert.Empty(renamed.GetProperty("boundPages").EnumerateObject());
        Assert.Equal(JsonValueKind.Object, renamed.GetProperty("boundPages").ValueKind);
    }
}
