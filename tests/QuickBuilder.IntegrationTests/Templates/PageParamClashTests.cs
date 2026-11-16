using System.Net;
using System.Text.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Templates;

/// <summary>
/// Contract rule 21 (016 FR-027, tasks T104): the paginated lists of a page and of the shared
/// components it shows read distinct page parameters. Draft saves are checked against the other
/// drafts, publishes against the live versions, and the refusal points into the document sent.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PageParamClashTests(DatabaseFixture database)
{
    private static string Paged(string id, Guid collection, string? param = null) =>
        TemplatesWorld.ListOf(
            id,
            collection,
            param is null ? """ "paging":{"pageSize":2} """ : $$""" "paging":{"pageSize":2,"param":"{{param}}"} """,
            TemplatesWorld.BoundHeading(id + "-title", """{"field":"f1"}"""));

    /// <summary>A page: one Section holding <paramref name="blocks"/>, then a reference to each component.</summary>
    private static string PageWith(string[] blocks, params Guid[] components) =>
        """{"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":["""
        + string.Join(',', blocks)
        + "]}"
        + string.Concat(components.Select((c, i) => $$$""",{"id":"ref{{{i}}}","type":"component-ref","props":{"componentId":"{{{c}}}"},"children":[]}"""))
        + "]}}";

    private static async Task<Guid> ComponentAsync(TemplatesWorld world, string name, string document, bool publish = false)
    {
        var id = (await PagesApi.CreateComponentOkAsync(world.Admin, world.Site, name)).GetProperty("id").GetGuid();
        var path = PagesApi.ComponentPath(world.Site, id);
        await PublishingApi.SaveAsync(world.Admin, path, document);
        if (publish)
        {
            await PublishingApi.PublishCurrentAsync(world.Admin, path);
        }

        return id;
    }

    private static async Task<JsonElement> RefusedAsync(HttpResponseMessage response)
    {
        var body = JsonElement.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pages.invalid_document", body.GetProperty("errorCode").GetString());
        Assert.Equal("page_param_taken", body.GetProperty("rule").GetString());
        return body;
    }

    [Fact]
    public async Task A_page_list_cannot_use_the_name_of_a_list_in_a_component_it_shows()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "param-page");
        var footer = await ComponentAsync(world, "Footer", TemplatesWorld.Document(Paged("people", world.People)));
        var page = await world.PageAsync("team");
        var path = PagesApi.PagePath(world.Site, page);

        var body = await RefusedAsync(await PagesApi.SaveCurrentDraftAsync(world.Admin, path, PageWith([Paged("posts", world.Posts)], footer)));

        Assert.Equal("/root/children/0/children/0/props/paging", body.GetProperty("pointer").GetString());
        Assert.Equal("page", body.GetProperty("param").GetString());
        Assert.Equal("Footer", body.GetProperty("with").GetString());

        // Its own name, and the page saves.
        await PublishingApi.SaveAsync(world.Admin, path, PageWith([Paged("posts", world.Posts, "posts")], footer));
    }

    [Fact]
    public async Task Two_components_on_one_page_cannot_page_under_one_name()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "param-two");
        var header = await ComponentAsync(world, "Header", TemplatesWorld.Document(Paged("news", world.Posts, "more")));
        var footer = await ComponentAsync(world, "Footer", TemplatesWorld.Document(Paged("people", world.People, "more")));
        var page = await world.PageAsync("team");

        var body = await RefusedAsync(await PagesApi.SaveCurrentDraftAsync(
            world.Admin, PagesApi.PagePath(world.Site, page), PageWith([], header, footer)));

        Assert.Equal("/root/children/2", body.GetProperty("pointer").GetString());
        Assert.Equal("more", body.GetProperty("param").GetString());
        Assert.Equal("Header", body.GetProperty("with").GetString());
    }

    [Fact]
    public async Task A_component_list_cannot_use_the_name_of_a_list_on_a_page_that_shows_it()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "param-component");
        var footer = await ComponentAsync(world, "Footer", TemplatesWorld.Document());
        var page = await world.PageAsync("team");
        await PublishingApi.SaveAsync(world.Admin, PagesApi.PagePath(world.Site, page), PageWith([Paged("posts", world.Posts)], footer));
        var title = (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, page))).GetProperty("title").GetString();

        var footerPath = PagesApi.ComponentPath(world.Site, footer);
        var body = await RefusedAsync(await PagesApi.SaveCurrentDraftAsync(world.Admin, footerPath, TemplatesWorld.Document(Paged("people", world.People))));

        Assert.Equal("/root/children/0/children/0/props/paging", body.GetProperty("pointer").GetString());
        Assert.Equal(title, body.GetProperty("with").GetString());

        await PublishingApi.SaveAsync(world.Admin, footerPath, TemplatesWorld.Document(Paged("people", world.People, "people")));
    }

    [Fact]
    public async Task A_component_publish_is_checked_against_the_live_pages_that_show_it()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "param-publish");
        var footer = await ComponentAsync(world, "Footer", TemplatesWorld.Document(), publish: true);
        var page = await world.PageAsync("team");
        var pagePath = PagesApi.PagePath(world.Site, page);
        await world.PublishDocumentAsync(page, PageWith([Paged("posts", world.Posts)], footer));

        // The page's draft stops showing the footer, so the footer's draft may take "page"; the
        // live page still shows the live footer, so publishing that draft is refused.
        await PublishingApi.SaveAsync(world.Admin, pagePath, PageWith([Paged("posts", world.Posts)]));
        var footerPath = PagesApi.ComponentPath(world.Site, footer);
        await PublishingApi.SaveAsync(world.Admin, footerPath, TemplatesWorld.Document(Paged("people", world.People)));

        var refused = await PublishingApi.PublishAsync(world.Admin, footerPath, await PublishingApi.DraftVersionAsync(world.Admin, footerPath));
        var body = await RefusedAsync(refused);
        Assert.Equal("/root/children/0/children/0/props/paging", body.GetProperty("pointer").GetString());

        await PublishingApi.SaveAsync(world.Admin, footerPath, TemplatesWorld.Document(Paged("people", world.People, "people")));
        await PublishingApi.PublishCurrentAsync(world.Admin, footerPath);
    }
}
