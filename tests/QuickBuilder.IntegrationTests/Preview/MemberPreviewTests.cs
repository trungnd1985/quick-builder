using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// User Story 1: a signed-in member previews a draft exactly as it will render (022 FR-001–FR-004,
/// FR-020, FR-029, FR-030; public-preview.md §2).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MemberPreviewTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _pricing;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "member-preview").ConfigureAwait(false);
        _pricing = await _world.PageAsync("pricing").ConfigureAwait(false);
        await _world.PublishDocumentAsync(_pricing, PublishingApi.HeadingDocument("Old prices")).ConfigureAwait(false);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, _pricing), PublishingApi.HeadingDocument("New prices"))
            .ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private string PagePreview(Guid pageId, Guid? site = null) => $"/_preview/sites/{site ?? _world.Site}/pages/{pageId}";

    private static async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false), response);
    }

    [Fact]
    public async Task A_draft_renders_as_a_complete_page_with_the_bar_while_the_public_page_is_unchanged()
    {
        var (status, html, _) = await GetAsync(_world.Admin, PagePreview(_pricing)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains(">New prices</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"qb-preview\"", html, StringComparison.Ordinal);
        Assert.Contains("Draft saved ", html, StringComparison.Ordinal);
        Assert.Contains($"/admin/sites/{_world.Site}/pages/{_pricing}/edit?share=1", html, StringComparison.Ordinal);

        var (_, published) = await _world.VisitAsync("/pricing").ConfigureAwait(true);
        Assert.Contains(">Old prices</h1>", published, StringComparison.Ordinal);
        Assert.DoesNotContain("New prices", published, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_never_published_previews_while_its_address_is_not_found()
    {
        var launch = await _world.PageAsync("launch").ConfigureAwait(true);
        await PublishingApi.SaveAsync(_world.Admin, PagesApi.PagePath(_world.Site, launch), PublishingApi.HeadingDocument("Coming soon")).ConfigureAwait(true);

        var (status, html, _) = await GetAsync(_world.Admin, PagePreview(launch)).ConfigureAwait(true);
        var (publicStatus, _) = await _world.VisitAsync("/launch").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Coming soon</h1>", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, publicStatus);
    }

    [Fact]
    public async Task An_item_draft_renders_through_its_template_and_a_template_page_shows_its_sample_item()
    {
        var blog = await _world.PageAsync("blog").ConfigureAwait(true);
        await _world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(true);
        var template = await _world.PageAsync("post", blog).ConfigureAwait(true);
        await _world.TemplateOkAsync(template, _world.Posts).ConfigureAwait(true);
        await _world.PublishDocumentAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("h", """{"field":"f1"}"""))).ConfigureAwait(true);
        var post = await _world.Items.PostAsync("hello", new JsonObject { ["f1"] = "Draft post title" }).ConfigureAwait(true);

        var (itemStatus, itemHtml, _) = await GetAsync(_world.Admin, $"/_preview/sites/{_world.Site}/items/{ItemsApi.Id(post)}").ConfigureAwait(true);
        var (templateStatus, templateHtml, _) = await GetAsync(_world.Admin, $"{PagePreview(template)}?item={ItemsApi.Id(post)}").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, itemStatus);
        Assert.Contains("Draft post title", itemHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, templateStatus);
        Assert.Contains("Draft post title", templateHtml, StringComparison.Ordinal);
        Assert.Contains("Showing the item", templateHtml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SystemRole.Editor)]
    [InlineData(SystemRole.Designer)]
    public async Task Every_site_role_may_preview(SystemRole role)
    {
        using var member = await _world.Cms.UserAsync((_world.Site, role)).ConfigureAwait(true);

        var (status, html, _) = await GetAsync(member, PagePreview(_pricing)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">New prices</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_callers_other_sites_members_and_other_sites_ids_all_get_the_same_not_found_page()
    {
        using var anonymous = _world.App.CreateAnonymousClient();
        using var otherSite = await _world.Cms.UserAsync((_world.Cms.SiteB, SystemRole.Designer)).ConfigureAwait(true);

        var bodies = new List<string>();
        foreach (var (client, path) in new[]
                 {
                     (anonymous, PagePreview(_pricing)),
                     (otherSite, PagePreview(_pricing)),
                     (_world.Admin, PagePreview(_pricing, _world.Cms.SiteB)),
                     (_world.Admin, PagePreview(Guid.NewGuid())),
                 })
        {
            var (status, body, _) = await GetAsync(client, path).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.DoesNotContain("New prices", body, StringComparison.Ordinal);
            bodies.Add(body);
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_item_whose_collection_has_no_template_cannot_be_previewed_as_a_page()
    {
        var person = await _world.Items.PersonAsync("Ada").ConfigureAwait(true);

        var (status, html, _) = await GetAsync(_world.Admin, $"/_preview/sites/{_world.Site}/items/{ItemsApi.Id(person)}").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("no template page", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_draft_that_breaks_the_page_schema_gives_members_the_reason()
    {
        var broken = await _world.PageAsync("broken").ConfigureAwait(true);
        await using (var scope = _world.App.Services.CreateAsyncScope())
        {
            var pages = scope.ServiceProvider.GetRequiredService<QuickBuilder.Pages.Infrastructure.PagesDbContext>();
            const string Invalid =
                """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"x","type":"no-such-block","props":{},"children":[]}]}}""";
            await pages.Database.ExecuteSqlAsync(
                    $"UPDATE pages.page_drafts SET document = {Invalid}::jsonb WHERE page_id = {broken}")
                .ConfigureAwait(true);
        }

        var (status, html, _) = await GetAsync(_world.Admin, PagePreview(broken)).ConfigureAwait(true);

        Assert.Equal((HttpStatusCode)422, status);
        Assert.Contains("This draft cannot be previewed", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_item_that_needs_attention_still_renders_with_a_warning()
    {
        var notes = await _world.Cms.CollectionAsync("notes", new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", required: true, settings: new { multiline = false, maxLength = 50 })),
        }).ConfigureAwait(true);
        var notesId = notes.GetProperty("id").GetGuid();
        var parent = await _world.PageAsync("notes").ConfigureAwait(true);
        var template = await _world.PageAsync("note", parent).ConfigureAwait(true);
        await _world.TemplateOkAsync(template, notesId).ConfigureAwait(true);
        var note = await ItemsApi.CreateOkAsync(
                _world.Admin, _world.Site, notesId, ItemsApi.Draft("empty", new JsonObject(), notes.GetProperty("schemaRevision").GetInt32()))
            .ConfigureAwait(true);

        var (status, html, _) = await GetAsync(_world.Admin, $"/_preview/sites/{_world.Site}/items/{ItemsApi.Id(note)}").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("This item needs attention", html, StringComparison.Ordinal);
        // The template was never published: its draft is shown, and the bar says so (PV-03).
        Assert.Contains("note” shown from its draft (never published)", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_media_and_the_stylesheet_are_served_through_the_preview()
    {
        var image = await MediaApi.UploadOkAsync(_world.Admin, _world.Site).ConfigureAwait(true);
        var file = image.PublicPath["/_media/".Length..];

        var (mediaStatus, _, mediaResponse) = await GetAsync(_world.Admin, $"/_preview/sites/{_world.Site}/media/{file}").ConfigureAwait(true);
        var (otherSiteStatus, _, _) = await GetAsync(_world.Admin, $"/_preview/sites/{_world.Cms.SiteB}/media/{file}").ConfigureAwait(true);
        var (cssStatus, css, _) = await GetAsync(
                _world.App.CreateAnonymousClient(), $"/_preview/assets/{QuickBuilder.Rendering.Rendering.BlockStylesheet.Instance.FileName}")
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, mediaStatus);
        Assert.Equal("no-store, private", mediaResponse.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, otherSiteStatus);
        Assert.Equal(HttpStatusCode.OK, cssStatus);
        Assert.Contains(".qb-", css, StringComparison.Ordinal);
    }
}
