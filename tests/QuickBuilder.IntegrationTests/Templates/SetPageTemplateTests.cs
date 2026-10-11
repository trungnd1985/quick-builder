using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>
/// Making a page a collection's template, changing it and removing it (016 US1, FR-001 to
/// FR-003, FR-008, FR-009, FR-036; contracts/templates-api.yaml; tasks T026).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SetPageTemplateTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_page_becomes_a_template_with_its_parents_address_and_the_slug_placeholder()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "tpl-set");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);

        var made = await world.TemplateOkAsync(post, world.Posts);

        Assert.Equal("/blog/{slug}", made.GetProperty("page").GetProperty("path").GetString());
        Assert.Equal(world.Posts, made.GetProperty("page").GetProperty("templateCollectionId").GetGuid());
        var node = await PagesApi.NodeAsync(world.Admin, world.Site, "/blog/{slug}");
        Assert.Equal(world.Posts, node.GetProperty("templateCollectionId").GetGuid());

        var outbox = await PagesOutboxAsync(world);
        Assert.Contains(outbox, e => e.Type == "Pages.PageTemplateChanged" && e.Payload.Contains(world.Posts.ToString(), StringComparison.Ordinal));
        Assert.Contains(outbox, e => e.Type == "Pages.PageAddressesChanged" && e.Payload.Contains("/blog/{slug}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_a_template_gives_the_page_its_own_address_again()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "tpl-remove");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        var made = await world.TemplateOkAsync(post, world.Posts);
        var version = made.GetProperty("page").GetProperty("version").GetInt32();

        var removed = await PagesApi.OkAsync(await world.Admin.DeleteAsync(new Uri($"{world.TemplatePath(post)}?version={version}", UriKind.Relative)));

        Assert.Equal("/blog/post", removed.GetProperty("page").GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("page").GetProperty("templateCollectionId").ValueKind);
    }

    [Theory]
    [InlineData("top-level", "pages.template_needs_parent", 400)]
    [InlineData("has-children", "pages.template_has_children", 409)]
    [InlineData("unknown-collection", "pages.unknown_collection", 400)]
    [InlineData("other-sites-collection", "pages.unknown_collection", 400)]
    public async Task A_template_change_breaking_a_rule_is_refused(string situation, string code, int status)
    {
        using var world = await TemplatesWorld.CreateAsync(database, $"tpl-refuse-{situation}");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        var collection = world.Posts;
        var page = post;

        switch (situation)
        {
            case "top-level":
                page = blog;
                break;
            case "has-children":
                await world.PageAsync("child", post);
                break;
            case "unknown-collection":
                collection = Guid.NewGuid();
                break;
            default:
                collection = (await world.Cms.CollectionAsync("elsewhere", new System.Text.Json.Nodes.JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["fields"] = new System.Text.Json.Nodes.JsonArray(Cms.CmsApi.Field("~1", "name", "text", settings: new { multiline = false, maxLength = 100 })),
                }, world.Cms.SiteB)).GetProperty("id").GetGuid();
                break;
        }

        var response = await world.SetTemplateAsync(page, collection);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_collection_has_one_template_and_a_parent_holds_one()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "tpl-one");
        var blog = await world.PageAsync("blog");
        var news = await world.PageAsync("news");
        var post = await world.PageAsync("post", blog);
        var story = await world.PageAsync("story", news);
        var sibling = await world.PageAsync("sibling", blog);
        await world.TemplateOkAsync(post, world.Posts);

        var sameCollection = await world.SetTemplateAsync(story, world.Posts);
        var sameParent = await world.SetTemplateAsync(sibling, world.People);

        Assert.Equal(HttpStatusCode.Conflict, sameCollection.StatusCode);
        var body = await sameCollection.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("pages.collection_has_template", post), (body.GetProperty("errorCode").GetString(), body.GetProperty("pageId").GetGuid()));
        Assert.Equal("pages.template_parent_taken", (await sameParent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task An_out_of_date_version_is_a_conflict()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "tpl-version");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);

        var response = await world.Admin.PutAsJsonAsync(new Uri(world.TemplatePath(post), UriKind.Relative), new { collectionId = world.Posts, version = 99 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("pages.out_of_date", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Only_designers_and_administrators_change_templates_and_strangers_see_nothing()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "tpl-auth");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        using var editor = await world.Cms.UserAsync((world.Site, SystemRole.Editor));
        using var designer = await world.Cms.UserAsync((world.Site, SystemRole.Designer));
        using var stranger = await world.Cms.UserAsync((world.Cms.SiteB, SystemRole.Designer));

        Assert.Equal(HttpStatusCode.Forbidden, (await world.SetTemplateAsync(post, world.Posts, editor)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.SetTemplateAsync(post, world.Posts, stranger)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync(new Uri(world.TemplatePath(post), UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.SetTemplateAsync(post, world.Posts, designer)).StatusCode);
    }

    [Fact]
    public async Task The_impact_counts_addresses_gained_and_lost_and_bindings_broken_and_changes_nothing()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "tpl-impact");
        await world.PublishedPostAsync("one");
        await world.PublishedPostAsync("two");
        await world.PublishedPersonAsync("Ann");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        await world.PublishDocumentAsync(post, TemplatesWorld.Document(
            TemplatesWorld.BoundHeading("h1", """{"field":"f1"}"""),
            TemplatesWorld.BoundHeading("h2", """{"field":"f3"}"""),
            TemplatesWorld.BoundHeading("h3", """{"item":"slug"}""")));

        var toPosts = await PagesApi.OkAsync(await world.Admin.GetAsync(new Uri($"{world.TemplatePath(post)}?collectionId={world.Posts}", UriKind.Relative)));

        Assert.Equal("/blog/{slug}", toPosts.GetProperty("pattern").GetString());
        Assert.Equal(JsonValueKind.Null, toPosts.GetProperty("refusal").ValueKind);
        Assert.True(toPosts.GetProperty("liveStaticAddressLost").GetBoolean());
        Assert.Equal(2, toPosts.GetProperty("itemsGainingAddress").GetInt32());
        Assert.Equal(0, toPosts.GetProperty("itemsLosingAddress").GetInt32());
        Assert.Equal("/blog/post", (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, post))).GetProperty("path").GetString());

        await world.TemplateOkAsync(post, world.Posts);

        // People have f1 but no f3: one binding breaks when the template shows people instead.
        var toPeople = await PagesApi.OkAsync(await world.Admin.GetAsync(new Uri($"{world.TemplatePath(post)}?collectionId={world.People}", UriKind.Relative)));
        var removing = await PagesApi.OkAsync(await world.Admin.GetAsync(new Uri(world.TemplatePath(post), UriKind.Relative)));

        Assert.Equal((2, 1, 1), (toPeople.GetProperty("itemsLosingAddress").GetInt32(), toPeople.GetProperty("itemsGainingAddress").GetInt32(), toPeople.GetProperty("brokenBindings").GetInt32()));
        Assert.Equal((2, 3, JsonValueKind.Null), (removing.GetProperty("itemsLosingAddress").GetInt32(), removing.GetProperty("brokenBindings").GetInt32(), removing.GetProperty("pattern").ValueKind));
    }

    [Fact]
    public async Task The_impact_names_the_refusal_a_change_would_meet()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "tpl-impact-refused");
        var blog = await world.PageAsync("blog");

        var impact = await PagesApi.OkAsync(await world.Admin.GetAsync(new Uri($"{world.TemplatePath(blog)}?collectionId={world.Posts}", UriKind.Relative)));

        Assert.Equal("pages.template_needs_parent", impact.GetProperty("refusal").GetString());
        Assert.Equal(JsonValueKind.Null, impact.GetProperty("pattern").ValueKind);
    }

    private static Task<List<(string Type, string Payload)>> PagesOutboxAsync(TemplatesWorld world) =>
        world.PagesOutboxAsync();
}
