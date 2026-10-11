using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Templates;

namespace QuickBuilder.IntegrationTests.Rendering;

/// <summary>
/// Lists that read enclosing scopes on published pages: the page's item on a template, an outer
/// card's item in a nested list, and multi-reference sources (016 US6, quickstart §6; tasks
/// T076). Query counting and the 200-query budget are unit-tested (<c>NestedListFetchTests</c>).
/// </summary>
/// <remarks>
/// Posts <c>a</c>, <c>b</c> (News) and <c>c</c> (Guides); <c>a</c> relates Carol, then a draft-only
/// person, then Bob, then Ann, in that stored order. A template at <c>/blog/{slug}</c>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed partial class NestedScopeTests(DatabaseFixture database) : IAsyncLifetime
{
    private TemplatesWorld _world = null!;
    private Guid _template;

    public async Task InitializeAsync()
    {
        _world = await TemplatesWorld.CreateAsync(database, "nested-scopes");
        var ann = ItemsApi.Id(await _world.PublishedPersonAsync("Ann"));
        var bob = ItemsApi.Id(await _world.PublishedPersonAsync("Bob"));
        var carol = ItemsApi.Id(await _world.PublishedPersonAsync("Carol"));
        var draftOnly = ItemsApi.Id(await _world.Items.PersonAsync("Dora"));

        await _world.PublishedPostAsync("a", new JsonObject
        {
            ["f1"] = $"{PagesApi.CanaryMarker}Post a",
            ["f8"] = "o1",
            ["f10"] = new JsonArray(carol.ToString(), draftOnly.ToString(), bob.ToString(), ann.ToString()),
        });
        await _world.PublishedPostAsync("b", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Post b", ["f8"] = "o1" });
        await _world.PublishedPostAsync("c", new JsonObject { ["f1"] = $"{PagesApi.CanaryMarker}Post c", ["f8"] = "o2" });

        var blog = await _world.PageAsync("blog");
        _template = await _world.PageAsync("post", blog);
        await _world.TemplateOkAsync(_template, _world.Posts);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [GeneratedRegex("<h1[^>]*>(?:zz-canary-)?([^<]*)</h1>")]
    private static partial Regex Heading();

    private static List<string> Headings(string body) => [.. Heading().Matches(body).Select(m => m.Groups[1].Value)];

    private static string Title(string id, string value) => TemplatesWorld.BoundHeading(id, value);

    [Fact]
    public async Task A_list_on_a_template_reads_the_page_item_and_leaves_the_page_item_out()
    {
        await _world.PublishDocumentAsync(_template, TemplatesWorld.Document(
            Title("title", """{"field":"f1"}"""),
            TemplatesWorld.ListOf(
                "more",
                _world.Posts,
                """ "filters":[{"field":"f8","op":"eq","value":{"field":"f8","scope":"page"}},{"item":"slug","op":"ne","value":{"current":true}}],"sort":[{"item":"slug","dir":"asc"}] """,
                Title("more-title", """{"field":"f1"}"""))));

        Assert.Equal(["Post a", "Post b"], Headings((await _world.VisitAsync("/blog/a")).Body));
        Assert.Equal(["Post b", "Post a"], Headings((await _world.VisitAsync("/blog/b")).Body));
        Assert.Equal(["Post c"], Headings((await _world.VisitAsync("/blog/c")).Body));
    }

    [Fact]
    public async Task A_list_in_a_list_reads_the_outer_cards_item_by_its_list_id()
    {
        var inner = TemplatesWorld.ListOf(
            "inner",
            _world.Posts,
            """ "filters":[{"field":"f8","op":"eq","value":{"field":"f8","scope":"outer"}},{"item":"slug","op":"ne","value":{"current":true,"scope":"outer"}}],"sort":[{"item":"slug","dir":"asc"}] """,
            Title("pair", """{"parts":[{"field":"f1"}," with ",{"field":"f1","scope":"outer"}]}"""));
        var page = await _world.PageAsync("pairs");
        await _world.PublishDocumentAsync(page, TemplatesWorld.Document(TemplatesWorld.ListOf(
            "outer",
            _world.Posts,
            """ "sort":[{"item":"slug","dir":"asc"}] """,
            inner)));

        var headings = Headings((await _world.VisitAsync("/pairs")).Body);

        Assert.Equal(["Post b with zz-canary-Post a", "Post a with zz-canary-Post b"], headings);
    }

    [Fact]
    public async Task A_multi_reference_source_keeps_stored_order_skips_unpublished_and_filters_and_limits_after()
    {
        await _world.PublishDocumentAsync(_template, TemplatesWorld.Document(
            Title("title", """{"field":"f1"}"""),
            TemplatesWorld.List("related", """{"field":"f10"}""", null, Title("person", """{"field":"f1"}"""))));
        var all = Headings((await _world.VisitAsync("/blog/a")).Body);

        await _world.PublishDocumentAsync(_template, TemplatesWorld.Document(
            Title("title", """{"field":"f1"}"""),
            TemplatesWorld.List(
                "related",
                """{"field":"f10"}""",
                """ "filters":[{"item":"slug","op":"ne","value":"carol"}],"limit":1 """,
                Title("person", """{"field":"f1"}"""))));
        var filtered = Headings((await _world.VisitAsync("/blog/a")).Body);
        var none = Headings((await _world.VisitAsync("/blog/b")).Body);

        Assert.Equal(["Post a", "Carol", "Bob", "Ann"], all);
        Assert.Equal(["Post a", "Bob"], filtered);
        Assert.Equal(["Post b"], none);
    }
}
