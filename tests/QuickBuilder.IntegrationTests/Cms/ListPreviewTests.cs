using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Cms;

/// <summary>
/// The editor's list preview: published data only, as the site would list it, with item
/// addresses and referenced items (016 US4, research TB-14; contracts/templates-api.yaml; tasks T062).
/// </summary>
/// <remarks>
/// Posts: <c>alpha</c> by Ann (its draft has moved on to Bob) and <c>beta</c> by Bob, both
/// published; <c>draft-only</c> never published. Posts have a live template at <c>/blog/{slug}</c>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ListPreviewTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _ann;
    private Guid _bob;
    private Guid _alpha;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "list-preview");
        _ann = ItemsApi.Id(await _world.PublishedPersonAsync("Ann"));
        _bob = ItemsApi.Id(await _world.PublishedPersonAsync("Bob"));

        var alpha = await _world.PublishedPostAsync("alpha", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Alpha", ["f9"] = _ann.ToString() });
        _alpha = ItemsApi.Id(alpha);
        await _world.PublishedPostAsync("beta", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Beta", ["f9"] = _bob.ToString() });
        await _world.Items.PostAsync("draft-only", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Draft only" });
        await ItemsApi.SaveOkAsync(_world.Admin, _world.Site, _world.Posts, _alpha,
            ItemsApi.Draft("alpha", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Alpha draft", ["f9"] = _bob.ToString() }, _world.Items.PostsSchemaRevision, ItemsApi.Revision(alpha)));

        var blog = await _world.PageAsync("blog");
        var post = await _world.PageAsync("post", blog);
        await _world.TemplateOkAsync(post, _world.Posts);
        await _world.PublishDocumentAsync(post, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}""")));
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    private string PreviewPath(Guid? site = null) => $"/api/v1/cms/sites/{site ?? _world.Site}/list-preview";

    private Task<HttpResponseMessage> PreviewAsync(object body, HttpClient? client = null, Guid? site = null) =>
        (client ?? _world.Admin).PostAsJsonAsync(new Uri(PreviewPath(site), UriKind.Relative), body);

    private object PostsList(object? filters = null) => new
    {
        list = new { source = new { collectionId = _world.Posts }, filters = filters ?? Array.Empty<object>(), sort = new[] { new { item = "slug", dir = "asc" } } },
    };

    private static List<string> Slugs(JsonElement preview) => [.. preview.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()!)];

    [Fact]
    public async Task Designers_and_editors_may_preview_and_a_member_without_a_role_finds_nothing()
    {
        using var designer = await _world.Cms.UserAsync((_world.Site, SystemRole.Designer));
        using var editor = await _world.Cms.UserAsync((_world.Site, SystemRole.Editor));
        using var stranger = await _world.Cms.UserAsync((_world.Cms.SiteB, SystemRole.Designer));

        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(PostsList(), designer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(PostsList(), editor)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(PostsList(), stranger)).StatusCode);
    }

    [Fact]
    public async Task Only_published_items_and_values_are_listed_with_their_addresses()
    {
        var preview = await PagesApi.OkAsync(await PreviewAsync(PostsList()));

        Assert.Equal(["alpha", "beta"], Slugs(preview));
        Assert.Equal(2, preview.GetProperty("total").GetInt32());
        var alpha = preview.GetProperty("items")[0];
        Assert.Equal($"{PagesApi.CanaryMarker}Alpha", alpha.GetProperty("values").GetProperty("values").GetProperty("f1").GetString());
        Assert.Equal("/blog/alpha", alpha.GetProperty("address").GetString());
        Assert.True(preview.GetProperty("schemas").TryGetProperty(_world.Posts.ToString(), out _));
    }

    [Fact]
    public async Task Referenced_items_come_back_for_the_fields_asked_for()
    {
        var body = new
        {
            list = new { source = new { collectionId = _world.Posts } },
            via = new[] { "f9" },
        };

        var preview = await PagesApi.OkAsync(await PreviewAsync(body));

        var referenced = preview.GetProperty("referenced");
        Assert.Equal($"{PagesApi.CanaryMarker}Ann", referenced.GetProperty(_ann.ToString()).GetProperty("name").GetString());
        Assert.True(referenced.TryGetProperty(_bob.ToString(), out _));
        Assert.True(preview.GetProperty("schemas").TryGetProperty(_world.People.ToString(), out _));
    }

    [Fact]
    public async Task The_page_scope_is_the_sample_items_draft()
    {
        // The sample's draft says Bob wrote it; its published copy still says Ann.
        var body = new
        {
            list = new
            {
                source = new { collectionId = _world.Posts },
                filters = new object[] { new { field = "f9", op = "eq", value = new { field = "f9", scope = "page" } } },
            },
            scopes = new Dictionary<string, Guid> { ["page"] = _alpha },
        };

        var preview = await PagesApi.OkAsync(await PreviewAsync(body));

        Assert.Equal(["beta"], Slugs(preview));
    }

    [Fact]
    public async Task A_broken_filter_matches_nothing()
    {
        var preview = await PagesApi.OkAsync(await PreviewAsync(PostsList(new object[] { new { field = "f99", op = "eq", value = "x" } })));

        Assert.Empty(Slugs(preview));
        Assert.Equal(0, preview.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task An_unknown_or_another_sites_collection_and_a_missing_scope_are_refused()
    {
        var unknown = new { list = new { source = new { collectionId = Guid.NewGuid() } } };
        var otherSite = new { list = new { source = new { collectionId = _world.Posts } } };
        var missingScope = new
        {
            list = new
            {
                source = new { collectionId = _world.Posts },
                filters = new object[] { new { field = "f9", op = "eq", value = new { field = "f9", scope = "page" } } },
            },
        };

        await PagesApi.RefusedAsync(await PreviewAsync(unknown), HttpStatusCode.BadRequest, "cms.invalid_list");
        await PagesApi.RefusedAsync(await PreviewAsync(otherSite, site: _world.Cms.SiteB), HttpStatusCode.BadRequest, "cms.invalid_list");
        await PagesApi.RefusedAsync(await PreviewAsync(missingScope), HttpStatusCode.BadRequest, "cms.invalid_list");
    }
}
