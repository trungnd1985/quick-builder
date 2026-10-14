using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Seo;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// Slugs, redirect sources and targets, titles and metadata never reach the logs, whatever 017
/// does with them: redirect matching and use counts, offers, import and export, the item
/// redirect handler (with its <c>oldSlug</c>), the sitemap and robots.txt (017 FR-038, tasks
/// T113; constitution Principle IX).
/// </summary>
/// <remarks>Everything written here carries the canary marker, so one scan of every log event finds a leak.</remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SeoContentNotLoggedTests(DatabaseFixture database)
{
    [Fact]
    public async Task No_address_title_or_metadata_appears_in_any_log_event()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "seo-not-logged");
        var marker = PagesApi.CanaryMarker;

        // A template with metadata and structured data, and an item whose slug changes with the offer.
        var blog = await world.PageAsync($"{marker}blog");
        await world.PublishDocumentAsync(blog, SeoApi.DocumentWithSeo($"{marker}Blog", $$"""{ "title": "{{marker}}meta", "description": "{{marker}}description" }"""));
        var template = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(template, world.Posts);
        await world.PublishDocumentAsync(template, SeoApi.DocumentWithSeo("Post",
            """{ "bind": { "title": { "value": { "field": "f1" } }, "description": { "value": { "field": "f99" } } }, "structuredData": { "type": "Article" } }"""));
        var post = await world.PublishedPostAsync($"{marker}old", new JsonObject { ["f1"] = $"{marker}Post" });
        var renamed = await ItemsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(post),
            ItemsApi.Draft($"{marker}new", new JsonObject { ["f1"] = $"{marker}Post" }, world.Items.PostsSchemaRevision, ItemsApi.Revision(post)));
        await ItemsApi.PostAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(renamed), "publish",
            new { revision = ItemsApi.Revision(renamed), cancelSchedule = false, redirectOldAddress = true });
        await DispatchAsync(world);

        // A page renamed with the offer, redirects made, imported, exported, followed and missed.
        var about = await world.PageAsync($"{marker}about");
        await world.PublishDocumentAsync(about, PublishingApi.HeadingDocument($"{marker}About"));
        var version = (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, about))).GetProperty("version").GetInt32();
        await world.Admin.PutAsJsonAsync(new Uri(PagesApi.PagePath(world.Site, about), UriKind.Relative),
            new { title = $"{marker}About", slug = $"{marker}company", version, redirectOldAddresses = true });
        await RedirectsApi.CreateOkAsync(world.Admin, world.Site, $"/{marker}campaign?utm={marker}", RedirectsApi.ToUrl($"https://elsewhere.test/{marker}"));
        await RedirectsApi.CreateAsync(world.Admin, world.Site, $"/{marker}loop", RedirectsApi.ToPath($"/{marker}loop"));
        using (var csv = new ByteArrayContent(Encoding.UTF8.GetBytes($"source,target,kind,match,note\n/{marker}imported,/{marker}company,301,exact,{marker}note\nnot-a-path,/x,301,exact,\n")))
        {
            csv.Headers.ContentType = new("text/csv");
            await world.Admin.PostAsync(new Uri($"{RedirectsApi.Root(world.Site)}/import", UriKind.Relative), csv);
        }

        await world.Admin.GetAsync(new Uri($"{RedirectsApi.Root(world.Site)}/export", UriKind.Relative));
        await SeoApi.SetSearchVisibilityAsync(world.Admin, world.Site, robotsRules: $"User-agent: {marker}bot\nDisallow: /");

        foreach (var path in new[]
        {
            $"/{marker}about", $"/{marker}imported", $"/{marker}campaign?utm={marker}", $"/{marker}blog/{marker}old",
            $"/{marker}blog/{marker}new", $"/{marker}Blog/", $"/{marker}missing", "/sitemap.xml", "/robots.txt",
        })
        {
            await RedirectsApi.VisitAsync(world.App, CmsScenario.HostA, path);
        }

        // The background save of use counts.
        await world.App.Services.GetRequiredService<QuickBuilder.Pages.Infrastructure.RedirectUsageFlusher>().FlushAsync(CancellationToken.None);

        var events = world.App.Logs.Events.Select(LogCapture.Render).ToList();
        Assert.NotEmpty(events);
        var leaked = events.FirstOrDefault(e => e.Contains(marker, StringComparison.OrdinalIgnoreCase));
        Assert.True(leaked is null, $"An address, title or metadata reached the logs: {leaked}");
    }

    private static async Task DispatchAsync(TemplatesWorld world)
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(world.App.Services);
        var settings = world.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None) > 0)
        {
        }
    }
}
