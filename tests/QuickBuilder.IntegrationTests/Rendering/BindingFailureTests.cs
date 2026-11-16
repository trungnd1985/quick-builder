using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Media.Contracts.Events;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Whatever happens to the data a page binds to, its addresses answer 200 with fallbacks or
/// hidden blocks, never an error (016 SC-007, FR-018, FR-038; tasks T083, T089).
/// </summary>
/// <remarks>
/// <c>/blog/hello</c> binds a post's title, reading time, author's name and cover; <c>/news</c>
/// lists News posts. Each change is followed by the events that refresh them, so the next visit
/// renders anew.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class BindingFailureTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_affected_address_keeps_answering_200_through_each_breaking_change()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "binding-failures");
        var ann = await world.PublishedPersonAsync("Ann");
        await world.PublishedPostAsync("hello", new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Hello",
            ["f3"] = 5,
            ["f6"] = new JsonObject { ["mediaId"] = world.Items.Image.ToString() },
            ["f8"] = "o1",
            ["f9"] = ItemsApi.Id(ann).ToString(),
        });

        var blog = await world.PageAsync("blog");
        var template = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(template, world.Posts);
        await world.PublishDocumentAsync(template, TemplatesWorld.Document(
            TemplatesWorld.BoundHeading("title", """{"field":"f1"}""", fallback: "No title"),
            TemplatesWorld.BoundHeading("time", """{"field":"f3"}""", fallback: "No time"),
            TemplatesWorld.BoundHeading("author", """{"field":"f1","via":"f9"}""", fallback: "No author"),
            """{"id":"cover","type":"image","props":{"alt":"Cover","bind":{"media":{"value":{"field":"f6"},"empty":"hide"}}},"children":[]}"""));
        var news = await world.PageAsync("news");
        var newsDocument = TemplatesWorld.Document(TemplatesWorld.ListOf(
            "l1", world.Posts, """ "filters":[{"field":"f8","op":"eq","value":"o1"}] """, TemplatesWorld.BoundHeading("t", """{"field":"f1"}""")));
        await world.PublishDocumentAsync(news, newsDocument);

        async Task AllAnswerAsync(string after)
        {
            foreach (var path in new[] { "/blog/hello", "/news" })
            {
                var (status, _) = await world.VisitAsync(path);
                Assert.True(status == HttpStatusCode.OK, $"{path} answered {(int)status} after {after}");
            }
        }

        Task SchemaChangedAsync() =>
            world.App.DeliverAsync(new CollectionChanged(Guid.CreateVersion7(), DateTimeOffset.UtcNow, world.Site, world.Posts, 0, true, true, null));

        async Task ChangeSchemaAsync(Action<JsonObject> change)
        {
            var collection = await CmsApi.GetOkAsync(world.Admin, world.Site, world.Posts);
            var schema = CmsApi.Schema(collection);
            change(schema);
            await CmsApi.ConfirmAndSaveAsync(world.Admin, world.Site, collection, schema);
            await SchemaChangedAsync();
        }

        JsonNode Field(JsonObject schema, string id) => CmsApi.Fields(schema).First(f => f!["id"]!.GetValue<string>() == id)!;

        await AllAnswerAsync("setting up");

        // A bound field removed.
        await ChangeSchemaAsync(schema =>
        {
            CmsApi.Fields(schema).Remove(Field(schema, "f1"));
            schema.Remove("titleFieldId");
        });
        await AllAnswerAsync("removing a bound field");
        Assert.Contains("No title", (await world.VisitAsync("/blog/hello")).Body, StringComparison.Ordinal);

        // A bound field retyped, number to text.
        await ChangeSchemaAsync(schema =>
        {
            var time = Field(schema, "f3").AsObject();
            time["type"] = "text";
            time.Remove("decimals");
            time.Remove("min");
            time["multiline"] = false;
            time["maxLength"] = 50;
        });
        await AllAnswerAsync("retyping a bound field");

        // The option a list filters on deleted: the filter breaks and matches nothing.
        await ChangeSchemaAsync(schema => Field(schema, "f8")["options"]!.AsArray().RemoveAt(0));
        await AllAnswerAsync("deleting a filtered option");
        Assert.Contains("Nothing yet", (await world.VisitAsync("/news")).Body, StringComparison.Ordinal);

        // A referenced item taken off the site.
        await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(world.Admin, world.Site, world.People, ItemsApi.Id(ann), ItemsApi.Revision(await ItemsApi.GetOkAsync(world.Admin, world.Site, world.People, ItemsApi.Id(ann)))));
        await AllAnswerAsync("unpublishing a referenced item");

        // A bound image deleted.
        Assert.Equal(HttpStatusCode.OK, (await QuickBuilder.IntegrationTests.Media.UsageScenario.DeleteAsync(world.Admin, world.Site, world.Items.Image, confirmInUse: true)).StatusCode);
        await world.App.DeliverAsync(new FileDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, world.Site, world.Items.Image, null));
        await AllAnswerAsync("deleting a bound image");

        // The collection deleted under a saved draft (the race TB-13 allows), behind the API's back.
        await using (var scope = world.App.Services.CreateAsyncScope())
        {
            var pages = scope.ServiceProvider.GetRequiredService<QuickBuilder.Pages.Infrastructure.PagesDbContext>();
            await pages.Database.ExecuteSqlRawAsync(
                "DELETE FROM cms.item_references WHERE site_id = {0}; DELETE FROM cms.items WHERE collection_id = {1}; DELETE FROM cms.collections WHERE id = {1};",
                world.Site,
                world.Posts);
        }

        await world.App.DeliverAsync(new CollectionDeleted(Guid.CreateVersion7(), DateTimeOffset.UtcNow, world.Site, world.Posts, null));
        var (newsStatus, newsBody) = await world.VisitAsync("/news");
        Assert.Equal(HttpStatusCode.OK, newsStatus);
        Assert.Contains("Nothing yet", newsBody, StringComparison.Ordinal);

        // The next save of that draft is refused by rule 14.
        var refused = await PagesApi.SaveCurrentDraftAsync(world.Admin, PagesApi.PagePath(world.Site, news), newsDocument);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("unknown_collection", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
