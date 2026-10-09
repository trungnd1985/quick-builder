using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// A rich text image needs alternative text, or to be marked decorative, before it is published;
/// a draft may hold one without (035 FR-014, US3 scenario 2, research RT-10).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublishRichTextImageTests(DatabaseFixture database)
{
    private const string Media = "0192f5a1-0000-7000-8000-000000000010";

    private static string Image(string attrs = "", string media = Media) =>
        """{"type":"image","attrs":{"media":{"mediaId":"MEDIA"}ATTRS}}"""
            .Replace("MEDIA", media, StringComparison.Ordinal)
            .Replace("ATTRS", attrs, StringComparison.Ordinal);

    /// <summary>A document whose second block is a Rich Text block holding <paramref name="blocks"/>.</summary>
    private static string Document(string blocks) =>
        """
        {"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s","type":"section","props":{},"children":[
          {"id":"h","type":"heading","props":{"text":"Shop"},"children":[]},
          {"id":"r","type":"rich-text","props":{"content":{"type":"doc","content":[BLOCKS]}},"children":[]}]}]}}
        """.Replace("BLOCKS", blocks, StringComparison.Ordinal);

    private const string At = "/root/children/0/children/1/props/content";

    [Fact]
    public async Task A_page_or_component_with_an_image_lacking_alternative_text_saves_but_does_not_publish()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "rt-image-publish");
        var page = PagesApi.PagePath(scenario.SiteA, (await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "home*\nshop"))["/shop"]);
        var component = PagesApi.ComponentPath(scenario.SiteA, (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer")).GetProperty("id").GetGuid());

        foreach (var path in new[] { page, component })
        {
            await PublishingApi.SaveAsync(scenario.Admin, path, Document($"{Image(",\"alt\":\"Front\"")},{Image()},{Image(",\"alt\":\"  \"")}"));

            var refused = await PagesApi.ProblemAsync(await PublishingApi.PublishAsync(
                scenario.Admin, path, await PublishingApi.DraftVersionAsync(scenario.Admin, path)));

            Assert.Equal((HttpStatusCode.UnprocessableEntity, "pages.image_alt_missing"), (refused.Status, refused.Code));
            Assert.Equal(
                [$"{At}/content/1", $"{At}/content/2"],
                refused.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("pointer").GetString()));

            await PublishingApi.SaveAsync(scenario.Admin, path, Document($"{Image(",\"alt\":\"Front\"")},{Image(",\"decorative\":true")}"));
            await PublishingApi.PublishCurrentAsync(scenario.Admin, path);
        }
    }

    [Fact]
    public async Task A_scheduled_publish_of_an_image_without_alternative_text_fails()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "rt-image-schedule");
        var about = (await PagesApi.SeedTreeAsync(scenario.Admin, scenario.SiteA, "about"))["/about"];
        var path = PagesApi.PagePath(scenario.SiteA, about);
        await PublishingApi.SaveAsync(scenario.Admin, path, Document(Image()));
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(
            scenario.Admin, path, "publish", DateTimeOffset.UtcNow.AddHours(1), await PublishingApi.DraftVersionAsync(scenario.Admin, path)));

        await scenario.ReadAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE pages.outbox_messages SET available_at_utc = now() - interval '1 second' WHERE event_type = 'Pages.PublishScheduleDue' AND completed_at_utc IS NULL"));
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(scenario.App.Services);
        var settings = scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None) > 0)
        {
        }

        var schedule = await scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).SingleAsync(s => s.SubjectId == about));
        Assert.Equal(ScheduleStates.Failed, schedule.State);
        Assert.Equal("pages.image_alt_missing", schedule.FailureCode);
        Assert.Equal(0, await scenario.ReadAsync(db => PagesScenario.Everything<PublishedVersion>(db).CountAsync(v => v.SubjectId == about)));
    }

    [Fact]
    public async Task An_item_with_an_image_lacking_alternative_text_saves_needs_attention_and_does_not_publish()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "rt-image-item");
        var body = JsonNode.Parse($$"""{"type":"doc","content":[{"type":"paragraph"},{{Image(media: scenario.Image.ToString())}}]}""");
        var created = await scenario.PostAsync("shop", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}shop", ["f2"] = body });

        var problem = Assert.Single(created.GetProperty("report").GetProperty("problems").EnumerateArray());
        Assert.Equal(("f2", "image_alt_missing", "/content/1"), (problem.GetProperty("fieldId").GetString(), problem.GetProperty("problem").GetString(), problem.GetProperty("path").GetString()));

        var refused = await PagesApi.ProblemAsync(await ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(created), ItemsApi.Revision(created)));
        Assert.Equal((HttpStatusCode.Conflict, "cms.item_needs_attention"), (refused.Status, refused.Code));
    }
}
