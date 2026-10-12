using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Rendering.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// User Story 6, SC-002, research PR-05: a publish through one instance is what every other
/// instance serves at its next request, though each keeps its own page cache.
/// </summary>
/// <remarks>
/// Two application instances over one database, each warmed first: a cached page that
/// survived the publish on the second instance — exception E-4 — would be caught here.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class RenderingAcrossInstancesTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Host = "instances.test";

    private QuickBuilderApp _first = null!;
    private QuickBuilderApp _second = null!;
    private HttpClient _admin = null!;
    private Guid _site;
    private string _pagePath = string.Empty;

    public async Task InitializeAsync()
    {
        var connection = await database.CreateDatabaseAsync("render-instances").ConfigureAwait(false);
        _first = new QuickBuilderApp(connection);
        _second = new QuickBuilderApp(connection);
        _ = _first.Services;
        _ = _second.Services;
        _admin = await _first.CreateAdminClientAsync().ConfigureAwait(false);

        var site = await MediaApi.CreateSiteAsync(_admin, "Instances", Host).ConfigureAwait(false);
        _site = site;
        var page = (await PagesApi.CreateOkAsync(_admin, site, "news").ConfigureAwait(false)).GetProperty("id").GetGuid();
        _pagePath = PagesApi.PagePath(site, page);
        await PublishingApi.SaveAsync(_admin, _pagePath, PublishingApi.HeadingDocument("First edition")).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_admin, _pagePath).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _first.Dispose();
        _second.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_publish_on_one_instance_is_served_by_the_other_at_its_next_request()
    {
        Assert.Contains("First edition", await VisitAsync(_first).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("First edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);

        // The second instance now answers from its cache.
        var renders = _second.Services.GetRequiredService<RenderMetrics>().Renders;
        Assert.Contains("First edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Equal(renders, _second.Services.GetRequiredService<RenderMetrics>().Renders);

        await PublishingApi.SaveAsync(_admin, _pagePath, PublishingApi.HeadingDocument("Second edition")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_admin, _pagePath).ConfigureAwait(true);

        Assert.Contains("Second edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("Second edition", await VisitAsync(_first).ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unpublish_on_one_instance_takes_the_page_down_on_the_other()
    {
        Assert.Contains("First edition", await VisitAsync(_second).ConfigureAwait(true), StringComparison.Ordinal);

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_admin, _pagePath).ConfigureAwait(true)).ConfigureAwait(true);

        var response = await _second.CreateClientForHost(Host).GetAsync(new Uri("/news", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_published_item_reaches_the_list_page_on_both_instances()
    {
        var (notes, schemaRevision, _) = await ListWorldAsync("notes-item").ConfigureAwait(true);
        Assert.Contains("Note one</h1>", await VisitAsync(_first, "/latest").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("Note one</h1>", await VisitAsync(_second, "/latest").ConfigureAwait(true), StringComparison.Ordinal);

        var item = await PublishNoteAsync(notes, schemaRevision, "two").ConfigureAwait(true);
        await _first.DeliverAsync(new ItemPublished(Guid.CreateVersion7(), DateTimeOffset.UtcNow, _site, notes, item, 1, "manual", false, null)).ConfigureAwait(true);

        Assert.Contains("Note two</h1>", await VisitAsync(_second, "/latest").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("Note two</h1>", await VisitAsync(_first, "/latest").ConfigureAwait(true), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_moved_template_changes_the_list_links_on_both_instances()
    {
        var (_, _, template) = await ListWorldAsync("notes-move").ConfigureAwait(true);
        Assert.Contains("href=\"/notes/one\"", await VisitAsync(_first, "/latest").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("href=\"/notes/one\"", await VisitAsync(_second, "/latest").ConfigureAwait(true), StringComparison.Ordinal);

        var archive = (await PagesApi.CreateOkAsync(_admin, _site, "archive").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var node = await PagesApi.OkAsync(await PagesApi.GetAsync(_admin, _site, template).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PagesApi.MoveAsync(_admin, _site, template, archive, null, node.GetProperty("version").GetInt32()).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Contains("href=\"/archive/one\"", await VisitAsync(_second, "/latest").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("href=\"/archive/one\"", await VisitAsync(_first, "/latest").ConfigureAwait(true), StringComparison.Ordinal);
    }

    /// <summary>
    /// 016: a "notes" collection with one published note, a live template at <c>/notes/{slug}</c>
    /// and <c>/latest</c> listing the notes with their titles and links.
    /// </summary>
    private async Task<(Guid Notes, int SchemaRevision, Guid Template)> ListWorldAsync(string slug)
    {
        var created = await CmsApi.CreateOkAsync(_admin, _site, slug).ConfigureAwait(false);
        var schema = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fields"] = new JsonArray(CmsApi.Field("~1", "title", "text", settings: new { multiline = false, maxLength = 100 })),
        };
        var notes = created.GetProperty("id").GetGuid();
        var saved = await CmsApi.SaveOkAsync(_admin, _site, notes, CmsApi.SaveBody(created, schema)).ConfigureAwait(false);
        var schemaRevision = saved.GetProperty("schemaRevision").GetInt32();
        await PublishNoteAsync(notes, schemaRevision, "one").ConfigureAwait(false);

        var parent = (await PagesApi.CreateOkAsync(_admin, _site, "notes").ConfigureAwait(false)).GetProperty("id").GetGuid();
        var template = (await PagesApi.CreateOkAsync(_admin, _site, "note", parent).ConfigureAwait(false)).GetProperty("id").GetGuid();
        var version = (await PagesApi.OkAsync(await PagesApi.GetAsync(_admin, _site, template).ConfigureAwait(false)).ConfigureAwait(false)).GetProperty("version").GetInt32();
        await PagesApi.OkAsync(await System.Net.Http.Json.HttpClientJsonExtensions.PutAsJsonAsync(
            _admin, new Uri($"{PagesApi.PagePath(_site, template)}/template", UriKind.Relative), new { collectionId = notes, version }).ConfigureAwait(false)).ConfigureAwait(false);
        await PublishAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}"""))).ConfigureAwait(false);

        var latest = (await PagesApi.CreateOkAsync(_admin, _site, "latest").ConfigureAwait(false)).GetProperty("id").GetGuid();
        await PublishAsync(latest, TemplatesWorld.Document(TemplatesWorld.ListOf("l1", notes, null, TemplatesWorld.TitleAndLink()))).ConfigureAwait(false);
        return (notes, schemaRevision, template);
    }

    private async Task<Guid> PublishNoteAsync(Guid notes, int schemaRevision, string slug)
    {
        var item = await ItemsApi.CreateOkAsync(_admin, _site, notes,
            ItemsApi.Draft(slug, new JsonObject { ["f1"] = $"Note {slug}" }, schemaRevision)).ConfigureAwait(false);
        await ItemsApi.PublishOkAsync(_admin, _site, notes, ItemsApi.Id(item), ItemsApi.Revision(item)).ConfigureAwait(false);
        return ItemsApi.Id(item);
    }

    private async Task PublishAsync(Guid pageId, string document)
    {
        var path = PagesApi.PagePath(_site, pageId);
        await PublishingApi.SaveAsync(_admin, path, document).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(_admin, path).ConfigureAwait(false);
    }

    private static Task<string> VisitAsync(QuickBuilderApp app, string path = "/news") =>
        app.CreateClientForHost(Host).GetStringAsync(new Uri(path, UriKind.Relative));
}
