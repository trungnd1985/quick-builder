using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Shared.Infrastructure.Outbox;
using Xunit;

namespace QuickBuilder.IntegrationTests.Seo;

/// <summary>
/// User Story 5: every page change that moves a live address offers redirects, created in the
/// same change when accepted, so the old address reaches the new content in one hop from the
/// next request (017 FR-028 to FR-032, SC-003; research SE-11).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RedirectOfferTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;

    public async Task InitializeAsync() => _world = await TemplatesWorld.CreateAsync(database, "seo-offers");

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> VisitAsync(string address) => RedirectsApi.VisitAsync(_world.App, CmsScenario.HostA, address);

    private async Task<Guid> LivePageAsync(string slug, Guid? parent = null)
    {
        var page = await _world.PageAsync(slug, parent);
        await _world.PublishDocumentAsync(page, PublishingApi.HeadingDocument(slug));
        return page;
    }

    private async Task<int> VersionAsync(Guid page) =>
        (await PagesApi.OkAsync(await PagesApi.GetAsync(_world.Admin, _world.Site, page))).GetProperty("version").GetInt32();

    private string PagePath(Guid page) => PagesApi.PagePath(_world.Site, page);

    private async Task<JsonElement> RenameAsync(Guid page, string slug, bool? redirect)
    {
        object body = redirect is { } accepted
            ? new { title = PagesApi.Title(slug), slug, version = await VersionAsync(page), redirectOldAddresses = accepted }
            : new { title = PagesApi.Title(slug), slug, version = await VersionAsync(page) };
        return await PagesApi.OkAsync(await _world.Admin.PutAsJsonAsync(new Uri(PagePath(page), UriKind.Relative), body));
    }

    /// <summary>The address <paramref name="from"/> sends visitors to, which answers without a further redirect.</summary>
    private async Task AssertOneHopAsync(string from, string to)
    {
        var response = await VisitAsync(from);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(to, response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync(to)).StatusCode);
    }

    [Fact]
    public async Task A_slug_change_redirects_the_page_and_its_live_children_from_the_next_request()
    {
        var about = await LivePageAsync("about");
        await LivePageAsync("team", about);

        var changed = await RenameAsync(about, "company", redirect: true);

        Assert.Equal(2, changed.GetProperty("redirects").GetProperty("created").GetInt32());
        await AssertOneHopAsync("/about", "/company");
        await AssertOneHopAsync("/about/team", "/company/team");
    }

    [Fact]
    public async Task Declining_or_omitting_the_offer_creates_nothing()
    {
        var declined = await LivePageAsync("declined");
        var omitted = await LivePageAsync("omitted");

        var no = await RenameAsync(declined, "declined-2", redirect: false);
        var none = await RenameAsync(omitted, "omitted-2", redirect: null);

        Assert.Equal(JsonValueKind.Null, no.GetProperty("redirects").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("redirects").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/declined")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/omitted")).StatusCode);
    }

    [Fact]
    public async Task A_move_redirects_the_old_address()
    {
        var about = await LivePageAsync("about");
        var team = await LivePageAsync("team", about);

        var moved = await PagesApi.OkAsync(await _world.Admin.PostAsJsonAsync(
            new Uri($"{PagePath(team)}/move", UriKind.Relative),
            new { parentId = (Guid?)null, afterPageId = (Guid?)null, version = await VersionAsync(team), redirectOldAddresses = true }));

        Assert.Equal(1, moved.GetProperty("redirects").GetProperty("created").GetInt32());
        await AssertOneHopAsync("/about/team", "/team");
    }

    [Fact]
    public async Task A_new_home_page_redirects_its_former_address_to_the_home_page()
    {
        await LivePageAsync("start");
        var about = await LivePageAsync("about");

        await PagesApi.OkAsync(await _world.Admin.PostAsJsonAsync(
            new Uri($"{PagePath(about)}/make-home", UriKind.Relative),
            new { version = await VersionAsync(about), redirectOldAddresses = true }));

        await AssertOneHopAsync("/about", "/");
    }

    [Fact]
    public async Task Moving_a_template_redirects_every_item_with_one_prefix_and_moving_it_back_turns_that_off()
    {
        var blog = await LivePageAsync("blog");
        var template = await _world.PageAsync("post", blog);
        await _world.TemplateOkAsync(template, _world.Posts);
        await _world.PublishDocumentAsync(template, PublishingApi.HeadingDocument("Post"));
        await _world.PublishedPostAsync("hello", new JsonObject { ["f1"] = PagesApi.Title("hello") });

        var away = await RenameAsync(blog, "news", redirect: true);

        Assert.Equal(2, away.GetProperty("redirects").GetProperty("created").GetInt32());
        await AssertOneHopAsync("/blog", "/news");
        await AssertOneHopAsync("/blog/hello", "/news/hello");

        var back = await RenameAsync(blog, "blog", redirect: true);

        Assert.Single(back.GetProperty("redirects").GetProperty("turnedOff").EnumerateArray());
        await AssertOneHopAsync("/news/hello", "/blog/hello");
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/blog")).StatusCode);
    }

    [Fact]
    public async Task Making_a_live_page_a_template_redirects_its_own_address_to_its_parent()
    {
        var blog = await LivePageAsync("blog");
        var post = await LivePageAsync("post", blog);

        var response = await _world.Admin.PutAsJsonAsync(
            new Uri(_world.TemplatePath(post), UriKind.Relative),
            new { collectionId = _world.Posts, version = await VersionAsync(post), redirectOldAddresses = true });
        var made = await PagesApi.OkAsync(response);

        Assert.Equal(1, made.GetProperty("redirects").GetProperty("created").GetInt32());
        await AssertOneHopAsync("/blog/post", "/blog");
    }

    [Fact]
    public async Task Unpublishing_with_a_page_chosen_redirects_the_address_to_it()
    {
        var home = await LivePageAsync("contact");
        var about = await LivePageAsync("about");

        await PagesApi.OkAsync(await _world.Admin.PostAsJsonAsync(
            new Uri($"{PagePath(about)}/unpublish", UriKind.Relative), new { redirectToPageId = home }));

        await AssertOneHopAsync("/about", "/contact");
    }

    [Fact]
    public async Task Unpublishing_without_a_body_still_works()
    {
        var about = await LivePageAsync("about");

        Assert.Equal(HttpStatusCode.OK, (await PublishingApi.UnpublishAsync(_world.Admin, PagePath(about))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync("/about")).StatusCode);
    }

    [Fact]
    public async Task Deleting_with_a_page_chosen_redirects_the_address_to_it()
    {
        var contact = await LivePageAsync("contact");
        var old = await _world.PageAsync("old");

        var response = await _world.Admin.DeleteAsync(new Uri($"{PagePath(old)}?version={await VersionAsync(old)}&redirectToPageId={contact}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertOneHopAsync("/old", "/contact");
    }

    [Fact]
    public async Task A_redirect_target_that_is_not_one_of_the_sites_pages_is_refused_and_nothing_changes()
    {
        var about = await LivePageAsync("about");

        var response = await _world.Admin.PostAsJsonAsync(new Uri($"{PagePath(about)}/unpublish", UriKind.Relative), new { redirectToPageId = Guid.NewGuid() });

        await PagesApi.RefusedAsync(response, HttpStatusCode.BadRequest, "redirects.target_invalid");
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync("/about")).StatusCode);
    }

    [Fact]
    public async Task A_scheduled_unpublish_applies_the_redirect_chosen_when_it_was_set()
    {
        var contact = await LivePageAsync("contact");
        var about = await LivePageAsync("about");
        await PagesApi.OkAsync(await _world.Admin.PutAsJsonAsync(
            new Uri($"{PagePath(about)}/schedules/unpublish", UriKind.Relative),
            new { dueAtUtc = DateTimeOffset.UtcNow.AddHours(1), redirectToPageId = contact }));

        await _world.Cms.WithDatabaseAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE pages.outbox_messages SET available_at_utc = now() - interval '1 second' WHERE event_type = 'Pages.PublishScheduleDue' AND completed_at_utc IS NULL"));
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_world.App.Services);
        var settings = _world.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None) > 0)
        {
        }

        await AssertOneHopAsync("/about", "/contact");
    }

    [Fact]
    public async Task Offered_redirects_are_recorded_as_offers()
    {
        var about = await LivePageAsync("about");

        await RenameAsync(about, "company", redirect: true);

        var origins = await _world.Cms.WithDatabaseAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT action AS \"Value\" FROM pages.redirect_changes WHERE site_id = {0}", _world.Site)
            .ToListAsync());
        Assert.Equal([RedirectChangeActions.Offered], origins);
    }
}
