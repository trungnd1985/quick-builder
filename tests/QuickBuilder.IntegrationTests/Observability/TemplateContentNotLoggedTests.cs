using System.Net.Http.Json;
using System.Text.Json.Nodes;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// Item values, names and slugs never reach the logs, whatever 016 does with them: rendering
/// item pages and lists, broken bindings (which do log), previews, refreshes, refused deletes
/// and address warnings (016 FR-038, tasks T099; constitution Principle IX).
/// </summary>
/// <remarks>
/// Every value, title and slug written here carries the canary marker, so one scan of every
/// captured log event finds any that leaked.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class TemplateContentNotLoggedTests(DatabaseFixture database)
{
    [Fact]
    public async Task No_item_value_name_or_slug_appears_in_any_log_event()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "content-not-logged");
        var marker = PagesApi.CanaryMarker;
        var author = await world.PublishedPersonAsync("Author");
        await world.PublishedPostAsync($"{marker}first", new JsonObject
        {
            ["f1"] = $"{marker}First title",
            ["f3"] = 7,
            ["f9"] = ItemsApi.Id(author).ToString(),
        });
        await world.PublishedPostAsync($"{marker}second", new JsonObject { ["f1"] = $"{marker}Second title" });

        // A template that binds values, follows a reference, and breaks one binding on purpose.
        var blog = await world.PageAsync("blog");
        var template = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(template, world.Posts);
        await world.PublishDocumentAsync(template, TemplatesWorld.Document(
            TemplatesWorld.BoundHeading("title", """{"field":"f1"}"""),
            TemplatesWorld.BoundHeading("author", """{"field":"f1","via":"f9"}"""),
            TemplatesWorld.BoundHeading("gone", """{"field":"f99"}"""),
            TemplatesWorld.ListOf("more", world.Posts, """ "filters":[{"item":"slug","op":"ne","value":{"current":true}}],"paging":{"pageSize":1} """,
                TemplatesWorld.BoundHeading("more-title", """{"field":"f1"}"""))));
        var list = await world.PageAsync("all");
        await world.PublishDocumentAsync(list, TemplatesWorld.Document(TemplatesWorld.ListOf(
            "l1", world.Posts, """ "filters":[{"field":"f1","op":"contains","value":"zz-canary-first"}] """, TemplatesWorld.TitleAndLink())));

        foreach (var path in new[] { $"/blog/{marker}first", $"/blog/{marker}second?page=2", $"/blog/{marker}missing", "/all" })
        {
            await world.VisitAsync(path);
        }

        // The editor's preview, a refresh, a refused delete, and an address warning.
        await world.Admin.PostAsJsonAsync(
            new Uri($"/api/v1/cms/sites/{world.Site}/list-preview", UriKind.Relative),
            new { list = new { source = new { collectionId = world.Posts }, filters = new[] { new { field = "f1", op = "eq", value = $"{marker}First title" } } }, via = new[] { "f9" } });
        await world.App.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, world.Site, world.Posts, Guid.NewGuid(), 1, "manual", false, null));
        var collection = await CmsApi.GetOkAsync(world.Admin, world.Site, world.Posts);
        await CmsApi.DeleteAsync(world.Admin, world.Site, world.Posts, collection.GetProperty("revision").GetInt32());
        await PagesApi.CreateAsync(world.Admin, world.Site, $"{marker}first", blog);
        await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, template));

        var events = world.App.Logs.Events.Select(LogCapture.Render).ToList();
        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.Contains("broken binding", StringComparison.OrdinalIgnoreCase));
        var leaked = events.FirstOrDefault(e => e.Contains(marker, StringComparison.OrdinalIgnoreCase));
        Assert.True(leaked is null, $"An item value, name or slug reached the logs: {leaked}");
    }
}
