using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// User Story 2: a member shares a time-limited preview link with a reviewer who has no account
/// (022 FR-005–FR-011, FR-018, FR-027; public-preview.md §3).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PreviewLinkTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Host = CmsScenario.HostA;
    private const string Gone = "This preview link is no longer available.";

    private readonly ManualClock _clock = new();
    private TemplatesWorld _world = null!;
    private Guid _pricing;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "preview-links", s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock)))
            .ConfigureAwait(false);
        _pricing = await _world.PageAsync("pricing").ConfigureAwait(false);
        await _world.PublishDocumentAsync(_pricing, PublishingApi.HeadingDocument("Old prices")).ConfigureAwait(false);
        await SaveAsync(_pricing, "New prices").ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private Task SaveAsync(Guid page, string heading) =>
        PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), PublishingApi.HeadingDocument(heading));

    private Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> OpenAsync(string path) =>
        PreviewLinksApi.OpenAsync(_world.App, path, Host);

    [Fact]
    public async Task An_editor_creates_a_link_that_anyone_opens_and_that_follows_the_latest_draft()
    {
        using var editor = await _world.Cms.UserAsync((_world.Site, SystemRole.Editor)).ConfigureAwait(true);

        var link = await PreviewLinksApi.CreateOkAsync(editor, _world.Site, "page", _pricing, _clock.GetUtcNow().AddDays(7), label: "Client")
            .ConfigureAwait(true);

        Assert.Matches("^https?://cms-a\\.test/_preview/l/[A-Za-z0-9_-]{43}$", link.Url);

        var (status, html, response) = await OpenAsync(link.Path).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">New prices</h1>", html, StringComparison.Ordinal);
        Assert.Contains("This link expires ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/admin/", html, StringComparison.Ordinal);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());

        // Clarification 1: the link shows the latest saved draft.
        await SaveAsync(_pricing, "Newer prices").ConfigureAwait(true);
        var (_, reloaded, _) = await OpenAsync(link.Path).ConfigureAwait(true);
        Assert.Contains(">Newer prices</h1>", reloaded, StringComparison.Ordinal);

        // The list shows it, with its label and when it was last opened, never its secret.
        var listed = await (await PreviewLinksApi.ListAsync(editor, _world.Site, "page", _pricing).ConfigureAwait(true))
            .Content.ReadAsStringAsync().ConfigureAwait(true);
        Assert.Contains("\"label\":\"Client\"", listed, StringComparison.Ordinal);
        Assert.Contains("\"lastOpenedAtUtc\":\"", listed, StringComparison.Ordinal);
        Assert.DoesNotContain(link.Url[^43..], listed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Last_opened_is_written_at_most_once_a_minute()
    {
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", _pricing, _clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);

        await OpenAsync(link.Path).ConfigureAwait(true);
        var first = await LastOpenedAsync(link.Id).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await OpenAsync(link.Path).ConfigureAwait(true);
        var second = await LastOpenedAsync(link.Id).ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromSeconds(31));
        await OpenAsync(link.Path).ConfigureAwait(true);
        var third = await LastOpenedAsync(link.Id).ConfigureAwait(true);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.True(third > second);
    }

    [Fact]
    public async Task An_item_link_and_a_template_page_link_with_a_sample_item_show_the_item()
    {
        var blog = await _world.PageAsync("blog").ConfigureAwait(true);
        await _world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(true);
        var template = await _world.PageAsync("post", blog).ConfigureAwait(true);
        await _world.TemplateOkAsync(template, _world.Posts).ConfigureAwait(true);
        await _world.PublishDocumentAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("h", """{"field":"f1"}"""))).ConfigureAwait(true);
        var post = ItemsApi.Id(await _world.Items.PostAsync("hello", new JsonObject { ["f1"] = "Draft post" }).ConfigureAwait(true));

        var itemLink = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "item", post, _clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);
        var templateLink = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", template, _clock.GetUtcNow().AddDays(1), sampleItemId: post)
            .ConfigureAwait(true);

        Assert.Contains("Draft post", (await OpenAsync(itemLink.Path).ConfigureAwait(true)).Body, StringComparison.Ordinal);
        Assert.Contains("Draft post", (await OpenAsync(templateLink.Path).ConfigureAwait(true)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_has_at_most_twenty_active_links_even_when_two_are_created_at_once()
    {
        for (var i = 0; i < 19; i++)
        {
            await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", _pricing, _clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);
        }

        var both = await Task.WhenAll(
                PreviewLinksApi.CreateAsync(_world.Admin, _world.Site, "page", _pricing, _clock.GetUtcNow().AddDays(1)),
                PreviewLinksApi.CreateAsync(_world.Admin, _world.Site, "page", _pricing, _clock.GetUtcNow().AddDays(1)))
            .ConfigureAwait(true);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], both.Select(r => r.StatusCode).Order());
        var refused = both.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("preview.link_limit", await refused.Content.ReadAsStringAsync().ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_requests_are_refused_with_their_codes()
    {
        var now = _clock.GetUtcNow();

        await PagesApi.RefusedAsync(await PreviewLinksApi.CreateAsync(_world.Admin, _world.Site, "page", _pricing, now.AddDays(31)).ConfigureAwait(true), HttpStatusCode.BadRequest, "preview.invalid_expiry").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await PreviewLinksApi.CreateAsync(_world.Admin, _world.Site, "page", _pricing, now.AddDays(1), label: new string('x', 81)).ConfigureAwait(true), HttpStatusCode.BadRequest, "preview.invalid_label").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await PreviewLinksApi.CreateAsync(_world.Admin, _world.Site, "page", Guid.NewGuid(), now.AddDays(1)).ConfigureAwait(true), HttpStatusCode.NotFound, "preview.not_found").ConfigureAwait(true);
        await PagesApi.RefusedAsync(await PreviewLinksApi.CreateAsync(_world.Admin, _world.Site, "page", _pricing, now.AddDays(1), versionNumber: 9).ConfigureAwait(true), HttpStatusCode.NotFound, "preview.version_not_retained").ConfigureAwait(true);
        var person = ItemsApi.Id(await _world.Items.PersonAsync("Ada").ConfigureAwait(true));
        await PagesApi.RefusedAsync(await PreviewLinksApi.CreateAsync(_world.Admin, _world.Site, "item", person, now.AddDays(1)).ConfigureAwait(true), HttpStatusCode.Conflict, "preview.no_template").ConfigureAwait(true);
    }

    [Fact]
    public async Task A_link_stops_at_its_expiry()
    {
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", _pricing, _clock.GetUtcNow().AddHours(1)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, (await OpenAsync(link.Path).ConfigureAwait(true)).Status);

        _clock.Advance(TimeSpan.FromHours(1));
        var (status, body, _) = await OpenAsync(link.Path).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains(Gone, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_is_gone_while_its_page_is_in_the_trash_and_back_after_a_restore()
    {
        var team = await _world.PageAsync("team").ConfigureAwait(true);
        await SaveAsync(team, "Our team").ConfigureAwait(true);
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", team, _clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);
        var version = (await PagesApi.OkAsync(await PagesApi.GetAsync(_world.Admin, _world.Site, team).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.DeleteAsync(_world.Admin, _world.Site, team, version).ConfigureAwait(true)).StatusCode);

        Assert.Contains(Gone, (await OpenAsync(link.Path).ConfigureAwait(true)).Body, StringComparison.Ordinal);

        var plan = await PagesApi.OkAsync(
                await _world.Admin.GetAsync(new Uri($"/api/v1/pages/sites/{_world.Site}/trash/pages/{team}/restore-plan", UriKind.Relative)).ConfigureAwait(true))
            .ConfigureAwait(true);
        var restored = await _world.Admin.PostAsJsonAsync(
                new Uri($"/api/v1/pages/sites/{_world.Site}/trash/pages/{team}/restore", UriKind.Relative),
                new { placement = (string?)null, confirmedAddress = plan.GetProperty("address").GetString(), publish = false })
            .ConfigureAwait(true);
        Assert.True(restored.IsSuccessStatusCode, await restored.Content.ReadAsStringAsync().ConfigureAwait(true));

        Assert.Contains(">Our team</h1>", (await OpenAsync(link.Path).ConfigureAwait(true)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_draft_that_breaks_the_page_schema_is_not_available_right_now()
    {
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", _pricing, _clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);
        await using (var scope = _world.App.Services.CreateAsyncScope())
        {
            const string Invalid =
                """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"x","type":"no-such-block","props":{},"children":[]}]}}""";
            await scope.ServiceProvider.GetRequiredService<QuickBuilder.Pages.Infrastructure.PagesDbContext>().Database
                .ExecuteSqlAsync($"UPDATE pages.page_drafts SET document = {Invalid}::jsonb WHERE page_id = {_pricing}")
                .ConfigureAwait(true);
        }

        var (status, body, response) = await OpenAsync(link.Path).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("not available right now", body, StringComparison.Ordinal);
        Assert.Equal("60", response.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task A_link_serves_the_media_its_page_uses_and_no_other_file_of_the_site()
    {
        var used = await MediaApi.UploadOkAsync(_world.Admin, _world.Site).ConfigureAwait(true);
        var other = await MediaApi.UploadOkAsync(_world.Admin, _world.Site).ConfigureAwait(true);
        var page = await _world.PageAsync("gallery").ConfigureAwait(true);
        var document = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":["""
            + $$$"""{"id":"i1","type":"image","props":{"media":{"mediaId":"{{{used.Id}}}"}},"children":[]}"""
            + "]}]}}";
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, page), document).ConfigureAwait(true);
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", page, _clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);

        var (_, html, _) = await OpenAsync(link.Path).ConfigureAwait(true);
        Assert.Contains($"{link.Path}/media/", html, StringComparison.Ordinal);

        var usedStatus = (await OpenAsync($"{link.Path}/media/{used.PublicPath["/_media/".Length..]}").ConfigureAwait(true)).Status;
        var (otherStatus, otherBody, _) = await OpenAsync($"{link.Path}/media/{other.PublicPath["/_media/".Length..]}").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, usedStatus);
        Assert.Equal(HttpStatusCode.NotFound, otherStatus);
        Assert.Contains(Gone, otherBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Links_created_before_the_operators_switch_stop_working_and_later_ones_work()
    {
        var clock = new ManualClock();
        var settings = new Dictionary<string, string> { ["Preview:LinksValidFrom"] = clock.GetUtcNow().AddMinutes(30).ToString("O") };
        using var world = await TemplatesWorld.CreateAsync(database, "preview-switch", s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)), settings)
            .ConfigureAwait(true);
        var page = await world.PageAsync("about").ConfigureAwait(true);
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, page), PublishingApi.HeadingDocument("Before")).ConfigureAwait(true);

        var before = await PreviewLinksApi.CreateOkAsync(world.Admin, world.Site, "page", page, clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);
        clock.Advance(TimeSpan.FromHours(1));
        var after = await PreviewLinksApi.CreateOkAsync(world.Admin, world.Site, "page", page, clock.GetUtcNow().AddDays(1)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await PreviewLinksApi.OpenAsync(world.App, before.Path, Host).ConfigureAwait(true)).Status);
        Assert.Equal(HttpStatusCode.OK, (await PreviewLinksApi.OpenAsync(world.App, after.Path, Host).ConfigureAwait(true)).Status);
    }

    private async Task<DateTimeOffset?> LastOpenedAsync(Guid linkId)
    {
        var listed = await (await PreviewLinksApi.ListAsync(_world.Admin, _world.Site, "page", _pricing).ConfigureAwait(false))
            .Content.ReadAsStringAsync().ConfigureAwait(false);
        using var body = JsonDocument.Parse(listed);
        var link = body.RootElement.EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == linkId);
        return link.GetProperty("lastOpenedAtUtc").ValueKind == JsonValueKind.Null ? null : link.GetProperty("lastOpenedAtUtc").GetDateTimeOffset();
    }
}
