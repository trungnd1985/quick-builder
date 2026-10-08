using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Images in rich text count as uses of their file, like Image blocks and image fields: in
/// drafts and published data, in pages, components and items (035 FR-018, research RT-11).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RichTextMediaUsageTests(DatabaseFixture database)
{
    private static string Image(Guid media) =>
        """{"type":"image","attrs":{"media":{"mediaId":"ID"},"alt":"Shop"}}""".Replace("ID", media.ToString(), StringComparison.Ordinal);

    private static string Document(Guid media) =>
        """
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s","type":"section","props":{},"children":[
          {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[IMAGE]}},"children":[]}]}]}}
        """.Replace("IMAGE", Image(media), StringComparison.Ordinal);

    [Fact]
    public async Task Pages_components_and_items_that_show_a_file_in_rich_text_are_its_usage()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "rt-media-usage");
        var file = scenario.Image;
        var page = (await PagesApi.SeedTreeAsync(scenario.Admin, scenario.Site, "home*\nshop"))["/shop"];
        var pagePath = PagesApi.PagePath(scenario.Site, page);
        await PublishingApi.SaveAsync(scenario.Admin, pagePath, Document(file));
        await PublishingApi.PublishCurrentAsync(scenario.Admin, pagePath);
        var component = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.Site, "Footer")).GetProperty("id").GetGuid();
        await PublishingApi.SaveAsync(scenario.Admin, PagesApi.ComponentPath(scenario.Site, component), Document(file));

        var body = () => JsonNode.Parse($$"""{"type":"doc","content":[{{Image(file)}}]}""");
        var draft = await scenario.PostAsync("draft", new JsonObject { ["f1"] = "zz-canary-draft", ["f2"] = body() });
        var live = await scenario.PostAsync("live", new JsonObject { ["f1"] = "zz-canary-live", ["f2"] = body() });
        await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(live), ItemsApi.Revision(live));

        var usage = await PagesApi.OkAsync(await scenario.Admin.GetAsync(new Uri($"/api/v1/media/sites/{scenario.Site}/files/{file}/usage", UriKind.Relative)));
        var summary = usage.GetProperty("summary");
        Assert.True(summary.GetProperty("inLiveUse").GetBoolean());
        var places = usage.GetProperty("places").EnumerateArray().Select(p => (p.GetProperty("kind").GetString(), p.GetProperty("id").GetGuid())).ToList();
        Assert.Contains(("page", page), places);
        Assert.Contains(("component", component), places);
        Assert.Contains(("item", ItemsApi.Id(draft)), places);
        Assert.Contains(("item", ItemsApi.Id(live)), places);

        // Deleting it warns, and the published page depends on it, so a change re-renders it.
        var inUse = await PagesApi.ProblemAsync(await scenario.Admin.DeleteAsync(new Uri($"/api/v1/media/sites/{scenario.Site}/files/{file}", UriKind.Relative)));
        Assert.Equal("media.in_use", inUse.Code);

        await using var scope = scenario.Cms.App.Services.CreateAsyncScope();
        var pages = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        Assert.True(await PagesScenario.Everything<LiveDependency>(pages).AnyAsync(d => d.SubjectId == page && d.TargetId == file));
    }
}
