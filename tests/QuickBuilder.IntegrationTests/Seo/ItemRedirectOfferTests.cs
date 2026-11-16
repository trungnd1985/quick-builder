using System.Diagnostics;
using System.Net;
using System.Text.Json;
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
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 5: an item's old address follows it when its slug changes, and goes to a chosen
/// page when it stops being live, through <c>Cms.ItemRedirectRequested</c> (017 FR-029 to
/// FR-032; research SE-11).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemRedirectOfferTests(DatabaseFixture database, ITestOutputHelper output) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _blog;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "seo-item-offers");
        _blog = await _world.PageAsync("blog");
        await _world.PublishDocumentAsync(_blog, PublishingApi.HeadingDocument("Blog"));
        var template = await _world.PageAsync("post", _blog);
        await _world.TemplateOkAsync(template, _world.Posts);
        await _world.PublishDocumentAsync(template, PublishingApi.HeadingDocument("Post"));
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> VisitAsync(string address) => RedirectsApi.VisitAsync(_world.App, CmsScenario.HostA, address);

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_world.App.Services);
        var settings = _world.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None) > 0)
        {
        }
    }

    private async Task<JsonElement> RenameAsync(JsonElement item, string slug) =>
        await ItemsApi.SaveOkAsync(_world.Admin, _world.Site, _world.Posts, ItemsApi.Id(item),
            ItemsApi.Draft(slug, new JsonObject { ["f1"] = PagesApi.Title(slug) }, _world.Items.PostsSchemaRevision, ItemsApi.Revision(item)));

    private Task<HttpResponseMessage> ActAsync(HttpClient client, JsonElement item, string action, object body) =>
        ItemsApi.PostAsync(client, _world.Site, _world.Posts, ItemsApi.Id(item), action, body);

    private Task<List<string>> RedirectEventsAsync() =>
        _world.Cms.WithDatabaseAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT payload AS \"Value\" FROM cms.outbox_messages WHERE event_type = 'Cms.ItemRedirectRequested'")
            .ToListAsync());

    [Fact]
    public async Task An_editor_publishing_a_new_slug_redirects_the_old_address_to_the_item_within_five_seconds()
    {
        var editor = await _world.Cms.UserAsync((_world.Site, SystemRole.Editor));
        var post = await _world.PublishedPostAsync("hello", new JsonObject { ["f1"] = PagesApi.Title("hello") });
        var renamed = await RenameAsync(post, "hello-again");

        var clock = Stopwatch.StartNew();
        var published = await PagesApi.OkAsync(await ActAsync(editor, renamed, "publish", new { revision = ItemsApi.Revision(renamed), cancelSchedule = false, redirectOldAddress = true }));
        await DispatchAsync();
        var response = await VisitAsync("/blog/hello");
        clock.Stop();
        output.WriteLine($"Item redirect in place after {clock.ElapsedMilliseconds} ms (FR-029: under 5 s).");

        Assert.True(published.GetProperty("redirectRequested").GetBoolean());
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/blog/hello-again", response.Headers.Location?.OriginalString);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_rollback_to_an_earlier_slug_redirects_the_newer_address()
    {
        var post = await _world.PublishedPostAsync("first", new JsonObject { ["f1"] = PagesApi.Title("first") });
        var renamed = await RenameAsync(post, "second");
        var second = await ItemsApi.PublishOkAsync(_world.Admin, _world.Site, _world.Posts, ItemsApi.Id(renamed), ItemsApi.Revision(renamed));

        await PagesApi.OkAsync(await ActAsync(_world.Admin, second, "versions/1/rollback", new { revision = ItemsApi.Revision(second), cancelSchedule = false, redirectOldAddress = true }));
        await DispatchAsync();

        var response = await VisitAsync("/blog/second");
        Assert.Equal("/blog/first", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unpublishing_or_deleting_with_a_page_chosen_redirects_the_address_to_it()
    {
        var gone = await _world.PublishedPostAsync("gone", new JsonObject { ["f1"] = PagesApi.Title("gone") });
        var deleted = await _world.PublishedPostAsync("deleted", new JsonObject { ["f1"] = PagesApi.Title("deleted") });

        await PagesApi.OkAsync(await ActAsync(_world.Admin, gone, "unpublish", new { revision = ItemsApi.Revision(gone), cancelSchedule = false, redirectToPageId = _blog }));
        var delete = await _world.Admin.DeleteAsync(new Uri(
            $"{ItemsApi.ItemPath(_world.Site, _world.Posts, ItemsApi.Id(deleted))}?revision={ItemsApi.Revision(deleted)}&redirectToPageId={_blog}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        await DispatchAsync();

        Assert.Equal("/blog", (await VisitAsync("/blog/gone")).Headers.Location?.OriginalString);
        Assert.Equal("/blog", (await VisitAsync("/blog/deleted")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task No_event_is_written_when_the_slug_did_not_change_or_the_offer_was_declined()
    {
        var same = await _world.PublishedPostAsync("same", new JsonObject { ["f1"] = PagesApi.Title("same") });
        var declined = await _world.PublishedPostAsync("declined", new JsonObject { ["f1"] = PagesApi.Title("declined") });

        await PagesApi.OkAsync(await ActAsync(_world.Admin, same, "publish", new { revision = ItemsApi.Revision(same), cancelSchedule = false, redirectOldAddress = true }));
        var renamed = await RenameAsync(declined, "declined-2");
        await PagesApi.OkAsync(await ActAsync(_world.Admin, renamed, "publish", new { revision = ItemsApi.Revision(renamed), cancelSchedule = false }));

        Assert.Empty(await RedirectEventsAsync());
    }

    [Fact]
    public async Task Without_a_live_template_the_item_had_no_address_and_nothing_is_created()
    {
        var people = await _world.PublishedPersonAsync("Ann");
        var saved = await ItemsApi.SaveOkAsync(_world.Admin, _world.Site, _world.People, ItemsApi.Id(people),
            ItemsApi.Draft("ann-2", new JsonObject { ["f1"] = PagesApi.Title("Ann") }, _world.Items.PeopleSchemaRevision, ItemsApi.Revision(people)));

        await PagesApi.OkAsync(await ItemsApi.PostAsync(_world.Admin, _world.Site, _world.People, ItemsApi.Id(saved), "publish",
            new { revision = ItemsApi.Revision(saved), cancelSchedule = false, redirectOldAddress = true }));
        await DispatchAsync();

        var redirects = await PagesApi.OkAsync(await _world.Admin.GetAsync(new Uri(RedirectsApi.Root(_world.Site), UriKind.Relative)));
        Assert.Equal(0, redirects.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_redelivered_event_creates_one_redirect_and_the_payload_has_the_documented_fields_only()
    {
        var post = await _world.PublishedPostAsync("once", new JsonObject { ["f1"] = PagesApi.Title("once") });
        var renamed = await RenameAsync(post, "once-more");
        await PagesApi.OkAsync(await ActAsync(_world.Admin, renamed, "publish", new { revision = ItemsApi.Revision(renamed), cancelSchedule = false, redirectOldAddress = true }));

        var payload = JsonElement.Parse(Assert.Single(await RedirectEventsAsync()));
        Assert.Equal(
            ["ActorUserId", "CollectionId", "Id", "ItemId", "OccurredAtUtc", "OldSlug", "Reason", "SiteId", "StatusCode", "TargetPageId"],
            payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        var requested = JsonSerializer.Deserialize<ItemRedirectRequested>(payload.GetRawText())!;
        await _world.App.DeliverAsync(requested);
        await _world.App.DeliverAsync(requested);

        var redirects = await PagesApi.OkAsync(await _world.Admin.GetAsync(new Uri(RedirectsApi.Root(_world.Site), UriKind.Relative)));
        Assert.Equal(1, redirects.GetProperty("total").GetInt32());
    }
}
