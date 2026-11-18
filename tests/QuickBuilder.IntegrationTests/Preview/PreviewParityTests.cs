using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Media;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Rendering.Features.Preview;
using Xunit;

namespace QuickBuilder.IntegrationTests.Preview;

/// <summary>
/// SC-001: a preview of a draft is what publishing the same content serves, apart from the bar,
/// the preview-only response rules and the rewritten addresses (022 FR-002, research PV-04).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class PreviewParityTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;

    public async Task InitializeAsync() =>
        _world = await TemplatesWorld.CreateAsync(database, "preview-parity").ConfigureAwait(false);

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_page_with_a_component_an_image_and_a_link_matches_its_published_render()
    {
        var contact = await _world.PageAsync("contact").ConfigureAwait(true);
        await _world.PublishDocumentAsync(contact, PublishingApi.HeadingDocument("Contact")).ConfigureAwait(true);

        var header = (await PagesApi.CreateComponentOkAsync(_world.Admin, _world.Site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = PagesApi.ComponentPath(_world.Site, header);
        await PublishingApi.SaveAsync(_world.Admin, headerPath, PublishingApi.ComponentDocument("Shared header")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(_world.Admin, headerPath).ConfigureAwait(true);

        var image = await MediaApi.UploadOkAsync(_world.Admin, _world.Site).ConfigureAwait(true);
        var about = await _world.PageAsync("about").ConfigureAwait(true);
        var document = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":["""
            + $$$"""{"id":"ref0","type":"component-ref","props":{"componentId":"{{{header}}}"},"children":[]},"""
            + """{"id":"s1","type":"section","props":{},"children":["""
            + """{"id":"h1","type":"heading","props":{"text":"About us","level":1},"children":[]},"""
            + $$$"""{"id":"i1","type":"image","props":{"media":{"mediaId":"{{{image.Id}}}"}},"children":[]},"""
            + $$$"""{"id":"b1","type":"button","props":{"label":"Write to us","link":{"type":"page","pageId":"{{{contact}}}"}},"children":[]}"""
            + "]}]}}";
        await _world.PublishDocumentAsync(about, document).ConfigureAwait(true);

        var (publicStatus, published) = await _world.VisitAsync("/about").ConfigureAwait(true);
        var preview = await _world.Admin.GetStringAsync(new Uri($"/_preview/sites/{_world.Site}/pages/{about}", UriKind.Relative)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, publicStatus);
        Assert.Contains("Shared header", preview, StringComparison.Ordinal);
        Assert.Contains($"/_preview/sites/{_world.Site}/media/", preview, StringComparison.Ordinal);
        Assert.Equal(Normalise(published), Normalise(preview));
    }

    [Fact]
    public async Task An_item_page_matches_its_published_render()
    {
        var blog = await _world.PageAsync("blog").ConfigureAwait(true);
        await _world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog")).ConfigureAwait(true);
        var template = await _world.PageAsync("post", blog).ConfigureAwait(true);
        await _world.TemplateOkAsync(template, _world.Posts).ConfigureAwait(true);
        await _world.PublishDocumentAsync(template, TemplatesWorld.Document(TemplatesWorld.BoundHeading("h", """{"field":"f1"}"""))).ConfigureAwait(true);
        var post = await _world.PublishedPostAsync("hello", new JsonObject { ["f1"] = "Hello world" }).ConfigureAwait(true);

        var (publicStatus, published) = await _world.VisitAsync("/blog/hello").ConfigureAwait(true);
        var preview = await _world.Admin.GetStringAsync(new Uri($"/_preview/sites/{_world.Site}/items/{ItemsApi.Id(post)}", UriKind.Relative))
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, publicStatus);
        Assert.Contains("Hello world", preview, StringComparison.Ordinal);
        Assert.Equal(Normalise(published), Normalise(preview));
    }

    /// <summary>
    /// A preview without what only a preview carries, mapped back to public addresses: the bar
    /// and its rules, the robots and referrer meta tags, preview media and stylesheet routes,
    /// and absolute links to the public site (spec FR-002, SC-001).
    /// </summary>
    private string Normalise(string html)
    {
        var start = html.IndexOf("<div class=\"qb-preview\"", StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = html.IndexOf("</div></div>\n", start, StringComparison.Ordinal) + "</div></div>\n".Length;
            html = html.Remove(start, end - start);
        }

        html = html.Replace(PreviewBar.Css, string.Empty, StringComparison.Ordinal)
            .Replace($"/_preview/sites/{_world.Site}/media/", "/_media/", StringComparison.Ordinal)
            .Replace("/_preview/assets/", "/_assets/", StringComparison.Ordinal);
        html = AbsoluteSiteLink().Replace(html, "href=\"/");
        return MetaToDrop().Replace(html, string.Empty);
    }

    [GeneratedRegex("href=\"https?://cms-a\\.test/")]
    private static partial Regex AbsoluteSiteLink();

    [GeneratedRegex("<meta name=\"(robots|referrer)\" content=\"[^\"]*\">\\n")]
    private static partial Regex MetaToDrop();
}
