using System.Net;
using System.Text.Json.Nodes;
using QuickBuilder.IntegrationTests.Cms;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>
/// Item pages on the public site, and the order addresses are matched in: reserved, static
/// page, template, not found (016 US1, FR-004 to FR-006, FR-012, FR-030; public-rendering.md §1;
/// tasks T027).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RouteMatchingTests(DatabaseFixture database)
{
    /// <summary>A published Blog page and a published post template under it, bound to title and reading time.</summary>
    private static async Task<(Guid Blog, Guid Post)> BlogAsync(TemplatesWorld world)
    {
        var blog = await world.PageAsync("blog");
        await world.PublishDocumentAsync(blog, PublishingApi.HeadingDocument("Blog"));
        var post = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(post, world.Posts);
        await world.PublishDocumentAsync(post, TemplatesWorld.Document(
            TemplatesWorld.BoundHeading("title", """{"field":"f1"}""", fallback: "Untitled"),
            """{"id":"reading","type":"text","props":{"text":"","bind":{"text":{"value":{"parts":["Read in ",{"field":"f3"}," min"]}}}},"children":[]}"""));
        return (blog, post);
    }

    [Fact]
    public async Task A_published_item_is_served_at_the_templates_pattern_with_its_published_values()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-item");
        await BlogAsync(world);
        await world.PublishedPostAsync("hello-world", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Hello world", ["f3"] = 7 });
        await world.PublishedPostAsync("autumn-update", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Autumn update" });

        var hello = await world.VisitAsync("/blog/hello-world");
        var autumn = await world.VisitAsync("/blog/Autumn-Update");

        Assert.Equal(HttpStatusCode.OK, hello.Status);
        Assert.Contains("Hello world</h1>", hello.Body, StringComparison.Ordinal);
        Assert.Contains("Read in 7 min", hello.Body, StringComparison.Ordinal);
        Assert.Contains($"<title>{PagesApi.CanaryMarker}Hello world</title>", hello.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, autumn.Status);
        Assert.Contains("Autumn update</h1>", autumn.Body, StringComparison.Ordinal);

        // No reading time: the template's own text is empty, so the text block falls back to it.
        Assert.DoesNotContain("Read in", autumn.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_only_unpublished_unknown_and_foreign_slugs_get_the_same_not_found_page()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-misses");
        await BlogAsync(world);
        await world.Items.PostAsync("draft-post");
        var gone = await world.PublishedPostAsync("gone-post");
        await ItemsApi.UnpublishAsync(world.Admin, world.Site, world.Posts, ItemsApi.Id(gone), ItemsApi.Revision(gone));
        await world.PublishedPersonAsync("Ann");

        var reference = await world.VisitAsync("/no-such-page");
        Assert.Equal(HttpStatusCode.NotFound, reference.Status);

        foreach (var path in new[] { "/blog/draft-post", "/blog/gone-post", "/blog/no-such-post", "/blog/ann", "/blog/{slug}", "/blog/hello--world" })
        {
            var visit = await world.VisitAsync(path);
            Assert.Equal((HttpStatusCode.NotFound, reference.Body), (visit.Status, visit.Body));
        }
    }

    [Fact]
    public async Task Another_sites_host_never_serves_this_sites_items()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-isolation");
        await BlogAsync(world);
        await world.PublishedPostAsync("hello-world");

        var visit = await world.VisitAsync("/blog/hello-world", CmsScenario.HostB);

        Assert.Equal(HttpStatusCode.NotFound, visit.Status);
    }

    [Fact]
    public async Task A_static_page_at_an_item_address_wins_and_uncovers_it_when_unpublished()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-static");
        var (blog, _) = await BlogAsync(world);
        await world.PublishedPostAsync("archive", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Archived post" });

        var archive = await world.PageAsync("archive", blog);
        await world.PublishDocumentAsync(archive, PublishingApi.HeadingDocument("Archive page"));
        var withPage = await world.VisitAsync("/blog/archive");

        await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, archive));
        var withoutPage = await world.VisitAsync("/blog/archive");

        Assert.Contains("Archive page", withPage.Body, StringComparison.Ordinal);
        Assert.Contains("Archived post", withoutPage.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_template_that_is_not_live_serves_no_items()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-template-down");
        var (_, post) = await BlogAsync(world);
        await world.PublishedPostAsync("hello-world");

        await PublishingApi.UnpublishAsync(world.Admin, PagesApi.PagePath(world.Site, post));

        Assert.Equal(HttpStatusCode.NotFound, (await world.VisitAsync("/blog/hello-world")).Status);
    }

    [Fact]
    public async Task Items_are_served_while_the_templates_parent_is_not_published()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-parent-down");
        var blog = await world.PageAsync("blog");
        var post = await world.PageAsync("post", blog);
        await world.TemplateOkAsync(post, world.Posts);
        await world.PublishDocumentAsync(post, TemplatesWorld.Document(TemplatesWorld.BoundHeading("title", """{"field":"f1"}""")));
        await world.PublishedPostAsync("hello-world");

        Assert.Equal(HttpStatusCode.NotFound, (await world.VisitAsync("/blog")).Status);
        Assert.Equal(HttpStatusCode.OK, (await world.VisitAsync("/blog/hello-world")).Status);
    }

    [Fact]
    public async Task A_link_to_the_template_page_itself_renders_as_plain_text()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-template-link");
        var (_, post) = await BlogAsync(world);
        var about = await world.PageAsync("about");
        await world.PublishDocumentAsync(about, TemplatesWorld.Document(
            $$$"""{"id":"b1","type":"button","props":{"label":"To template","link":{"type":"page","pageId":"{{{post}}}"}},"children":[]}"""));

        var visit = await world.VisitAsync("/about");

        Assert.Contains("<span class=\"qb-button", visit.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("{slug}", visit.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_the_collections_address_name_leaves_item_addresses_alone()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-address-name");
        await BlogAsync(world);
        await world.PublishedPostAsync("hello-world");
        var collection = await CmsApi.GetOkAsync(world.Admin, world.Site, world.Posts);

        await CmsApi.SaveOkAsync(world.Admin, world.Site, world.Posts, CmsApi.SaveBody(collection, CmsApi.Schema(collection), slug: "articles"));

        Assert.Equal(HttpStatusCode.OK, (await world.VisitAsync("/blog/hello-world")).Status);
    }

    [Fact]
    public async Task A_one_segment_address_is_never_an_item()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "route-one-segment");
        await BlogAsync(world);
        await world.PublishedPostAsync("blog-post");

        Assert.Equal(HttpStatusCode.NotFound, (await world.VisitAsync("/blog-post")).Status);
    }
}
