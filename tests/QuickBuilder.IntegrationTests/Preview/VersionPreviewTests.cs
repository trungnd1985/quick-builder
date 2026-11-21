using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Rendering.Features.Preview;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// User Story 5: a member previews and shares a specific past version, shown as a rollback to it
/// would publish it now (022 FR-028, SC-007).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class VersionPreviewTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _page;
    private Guid _header;
    private string _path = string.Empty;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "preview-versions").ConfigureAwait(false);
        _header = (await PagesApi.CreateComponentOkAsync(_world.Admin, _world.Site, "Header").ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PublishComponentAsync("Header today").ConfigureAwait(false);

        _page = await _world.PageAsync("pricing").ConfigureAwait(false);
        _path = PagesApi.PagePath(_world.Site, _page);
        foreach (var heading in new[] { "Prices one", "Prices two", "Prices three" })
        {
            await PublishingApi.SaveAsync(_world.Admin, _path, PublishingApi.HeadingDocument(heading, _header)).ConfigureAwait(false);
            await PublishingApi.PublishCurrentAsync(_world.Admin, _path).ConfigureAwait(false);
        }

        await PublishingApi.SaveAsync(_world.Admin, _path, PublishingApi.HeadingDocument("Draft prices", _header)).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private async Task PublishComponentAsync(string text)
    {
        var path = PagesApi.ComponentPath(_world.Site, _header);
        await PublishingApi.SaveAsync(_world.Admin, path, PublishingApi.ComponentDocument(text)).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_world.Admin, path).ConfigureAwait(false);
    }

    private Task<string> MemberAsync(int version) =>
        _world.Admin.GetStringAsync(new Uri($"/_preview/sites/{_world.Site}/pages/{_page}?version={version}", UriKind.Relative));

    [Fact]
    public async Task Each_version_shows_its_own_content_with_todays_shared_parts_and_names_itself()
    {
        await PublishComponentAsync("Header changed later").ConfigureAwait(true);

        foreach (var (number, heading) in new[] { (1, "Prices one"), (2, "Prices two"), (3, "Prices three") })
        {
            var html = await MemberAsync(number).ConfigureAwait(true);
            Assert.Contains($">{heading}</h1>", html, StringComparison.Ordinal);
            Assert.Contains("Header changed later", html, StringComparison.Ordinal);
            Assert.Contains($"Version {number}, published ", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Draft prices", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_link_to_a_version_keeps_showing_that_version_and_nothing_else()
    {
        var link = await PreviewLinksApi.CreateOkAsync(_world.Admin, _world.Site, "page", _page, DateTimeOffset.UtcNow.AddDays(1), versionNumber: 1)
            .ConfigureAwait(true);

        await PublishingApi.SaveAsync(_world.Admin, _path, PublishingApi.HeadingDocument("Even newer", _header)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_world.Admin, _path).ConfigureAwait(true);

        var (status, html, _) = await PreviewLinksApi.OpenAsync(_world.App, link.Path, CmsScenario.HostA).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Prices one</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Even newer", html, StringComparison.Ordinal);

        var (_, withQuery, _) = await PreviewLinksApi.OpenAsync(_world.App, $"{link.Path}?version=3", CmsScenario.HostA).ConfigureAwait(true);
        Assert.Contains(">Prices one</h1>", withQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_a_rollback_the_public_page_is_what_the_version_preview_showed()
    {
        var preview = await MemberAsync(1).ConfigureAwait(true);

        Assert.True((await PublishingApi.RollbackAsync(_world.Admin, _path, 1).ConfigureAwait(true)).IsSuccessStatusCode);
        var (_, published) = await _world.VisitAsync("/pricing").ConfigureAwait(true);

        Assert.Equal(PageRoot(published), PageRoot(preview));
    }

    [Fact]
    public async Task An_items_version_is_previewed_and_a_version_out_of_retention_is_gone()
    {
        var blog = await _world.PageAsync("blog").ConfigureAwait(true);
        await _world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(true);
        var template = await _world.PageAsync("post", blog).ConfigureAwait(true);
        await _world.TemplateOkAsync(template, _world.Posts).ConfigureAwait(true);
        await _world.PublishDocumentAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("h", """{"field":"f1"}"""))).ConfigureAwait(true);
        var created = await _world.Items.PostAsync("hello", new JsonObject { ["f1"] = "First title" }).ConfigureAwait(true);
        var post = ItemsApi.Id(created);
        var published = await ItemsApi.PublishOkAsync(_world.Admin, _world.Site, _world.Posts, post, ItemsApi.Revision(created)).ConfigureAwait(true);
        await ItemsApi.SaveOkAsync(
                _world.Admin, _world.Site, _world.Posts, post,
                ItemsApi.Draft("hello", new JsonObject { ["f1"] = "Second title" }, _world.Items.PostsSchemaRevision, ItemsApi.Revision(published)))
            .ConfigureAwait(true);

        var item = await _world.Admin.GetStringAsync(new Uri($"/_preview/sites/{_world.Site}/items/{post}?version=1", UriKind.Relative)).ConfigureAwait(true);
        Assert.Contains("First title", item, StringComparison.Ordinal);
        Assert.DoesNotContain("Second title", item, StringComparison.Ordinal);

        // A version not kept (here: never made) is gone for members and for links alike.
        var missing = await _world.Admin.GetAsync(new Uri($"/_preview/sites/{_world.Site}/pages/{_page}?version=40", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            NotAvailablePage.Gone.Body.ToArray(),
            await missing.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }

    /// <summary>The page's own content, without the head, the bar or addresses that differ by surface.</summary>
    private static string PageRoot(string html)
    {
        var start = html.IndexOf("<div class=\"qb-canvas qb-page\">", StringComparison.Ordinal);
        return html[start..html.LastIndexOf("</body>", StringComparison.Ordinal)];
    }
}
