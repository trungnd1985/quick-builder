using System.Text;
using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.UnitTests.Rendering.Binding;

namespace QuickBuilder.UnitTests.Rendering.Lists;

/// <summary>
/// A Collection list renders its card once per listed item, bound to that item, in columns, or
/// its empty state in place of the list (016 FR-022; public-rendering.md §5; tasks T061). The
/// HTML is checked in under <c>tests/fixtures/rendering/lists/</c>; set <c>QB_UPDATE_GOLDEN=1</c>
/// to rewrite it, then review every changed file.
/// </summary>
/// <remarks>
/// The world is the binding contract's <c>rich-image-link</c> case: posts (<c>…c001</c>) with a
/// live template at <c>/blog/{slug}</c>. The listed items are its page item under three slugs.
/// </remarks>
public sealed class CollectionListRenderTests
{
    private const string Posts = "0192f5a0-0000-7000-8000-00000000c001";

    private static readonly ResolveCase World = ResolveCase.Load("rich-image-link");

    private static string Document(string listProps, string? cardBlocks = null) =>
        """{"schemaVersion":1,"bindingVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"s1","type":"section","props":{},"children":[{"id":"posts","type":"collection-list","props":"""
        + listProps
        + ""","children":[{"id":"posts-card","type":"list-card","props":{},"children":["""
        + (cardBlocks ?? """{"id":"title","type":"heading","props":{"text":"Fallback","level":3,"bind":{"text":{"value":{"field":"f1"}}}},"children":[]},{"id":"go","type":"button","props":{"label":"Read","bind":{"link":{"value":{"item":"url"}}}},"children":[]}""")
        + """]},{"id":"posts-empty","type":"list-empty","props":{},"children":[{"id":"none","type":"text","props":{"text":"Nothing to show yet."},"children":[]}]}]}]}]}}""";

    private static PublishedItem Post(string slug, string title)
    {
        var page = World.Root.GetProperty("pageItem");
        var values = JsonSerializer.SerializeToElement(new { schemaVersion = 1, values = new { f1 = title } });
        return ResolveCase.Item(page) with { Id = Guid.NewGuid(), Slug = slug, Name = title, Values = values };
    }

    private static readonly IReadOnlyList<PublishedItem> ThreePosts =
    [
        Post("linen-care", "Caring for linen") with { Id = Guid.Parse("0192f5a0-0000-7000-8000-0000000a0001") },
        Post("dyeing", "Natural dyes") with { Id = Guid.Parse("0192f5a0-0000-7000-8000-0000000a0002") },
        Post("weaving", "Weaving <basics>") with { Id = Guid.Parse("0192f5a0-0000-7000-8000-0000000a0003") },
    ];

    private static string Render(
        string document,
        IReadOnlyList<PublishedItem>? items,
        bool withTemplate = true,
        bool onTemplatePage = false,
        int total = -1,
        IReadOnlyDictionary<string, int>? pageNumbers = null)
    {
        using var parsed = JsonDocument.Parse(document);
        var inputs = World.Inputs();
        var pageItem = onTemplatePage ? inputs.Data!.PageItem : null;
        var key = ResolvedData.ListKey("posts", pageItem is null ? [] : [pageItem.Id]);
        var data = new ResolvedData
        {
            PageItem = pageItem,
            Items = inputs.Data!.Items,
            Schemas = inputs.Data.Schemas,
            TemplatePatterns = withTemplate ? inputs.Data.TemplatePatterns : new Dictionary<Guid, string>(),
            Lists = items is null
                ? new Dictionary<string, PublishedItemPage>(StringComparer.Ordinal)
                : new Dictionary<string, PublishedItemPage>(StringComparer.Ordinal) { [key] = new(items, total < 0 ? items.Count : total) },
            PageNumbers = pageNumbers ?? new Dictionary<string, int>(StringComparer.Ordinal),
            ListPath = "/blog",
        };

        return Encoding.UTF8.GetString(
            PageResponder.Render(inputs with { Document = parsed.RootElement.Clone(), Data = data }, RenderHarness.Renderer).Html);
    }

    private static void Golden(string name, string html)
    {
        var golden = RepositoryFiles.Fixtures("lists", $"{name}.html");
        if (RepositoryFiles.UpdateGolden)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, html);
            return;
        }

        Assert.True(File.Exists(golden), $"No golden file for {name}. Run the tests with QB_UPDATE_GOLDEN=1, then review it.");
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), html);
    }

    [Fact]
    public void Cards_repeat_per_item_bound_to_it_in_columns_with_item_page_links()
    {
        var html = Render(Document($$$"""{"source":{"collectionId":"{{{Posts}}}"},"columns":2,"gap":{"token":"space-lg"}}"""), ThreePosts);

        Golden("cards", html);
        Assert.Contains("<ul class=\"qb-list\"", html, StringComparison.Ordinal);
        Assert.Equal(3, html.Split("<li class=\"qb-list__card\">").Length - 1);
        Assert.Contains("Caring for linen</h3>", html, StringComparison.Ordinal);
        Assert.Contains("Weaving &lt;basics&gt;</h3>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/site/blog/dyeing\"", html, StringComparison.Ordinal);
        Assert.Contains("--qb-cols:2;--qb-cols-tablet:2;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing to show yet.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Four_columns_step_down_to_two_at_tablet_width()
    {
        var html = Render(Document($$$"""{"source":{"collectionId":"{{{Posts}}}"},"columns":4}"""), ThreePosts);

        Assert.Contains("--qb-cols:4;--qb-cols-tablet:2;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_result_shows_the_empty_state_in_place_of_the_list()
    {
        var html = Render(Document($$$"""{"source":{"collectionId":"{{{Posts}}}"}}"""), []);

        Golden("empty", html);
        Assert.Contains("<div class=\"qb-list__empty\">", html, StringComparison.Ordinal);
        Assert.Contains("Nothing to show yet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul class=\"qb-list\"", html, StringComparison.Ordinal);
        Assert.Equal(html, Render(Document($$$"""{"source":{"collectionId":"{{{Posts}}}"}}"""), null));
    }

    [Fact]
    public void Without_a_live_template_the_item_page_button_has_no_address()
    {
        var html = Render(Document($$$"""{"source":{"collectionId":"{{{Posts}}}"}}"""), ThreePosts, withTemplate: false);

        Golden("no-template", html);
        Assert.DoesNotContain("/blog/", html, StringComparison.Ordinal);
        Assert.Contains("Caring for linen</h3>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Cards_read_the_page_item_through_the_page_scope_on_a_template_page()
    {
        var card = """{"id":"title","type":"heading","props":{"text":"Fallback","level":3,"bind":{"text":{"value":{"parts":[{"field":"f1"},", from ",{"field":"f1","scope":"page"}]}}}},"children":[]}""";

        var html = Render(Document($$$"""{"source":{"collectionId":"{{{Posts}}}"}}""", card), ThreePosts.Take(1).ToList(), onTemplatePage: true);

        var pageTitle = World.Root.GetProperty("pageItem").GetProperty("values").GetProperty("values").GetProperty("f1").GetString()!;
        Assert.Contains($"Caring for linen, from {System.Net.WebUtility.HtmlEncode(pageTitle)}</h3>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_paginated_list_writes_its_pager_with_links_that_keep_other_lists()
    {
        var html = Render(
            Document($$$"""{"source":{"collectionId":"{{{Posts}}}"},"paging":{"pageSize":3},"layerLabel":"Latest posts"}"""),
            ThreePosts,
            total: 60,
            pageNumbers: new Dictionary<string, int>(StringComparer.Ordinal) { ["page"] = 10, ["people"] = 2 });

        Golden("paged", html);
        Assert.Contains("<nav class=\"qb-pager\" aria-label=\"Latest posts\">", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/site/blog?page=9&amp;people=2\" rel=\"prev\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/site/blog?page=10&amp;people=2\" aria-current=\"page\">10</a>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/site/blog?people=2\">1</a>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/site/blog?page=20&amp;people=2\">20</a>", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("qb-pager__gap").Length - 1);
    }

    [Fact]
    public void Past_the_last_page_the_empty_state_shows_with_the_way_back()
    {
        var html = Render(
            Document($$$"""{"source":{"collectionId":"{{{Posts}}}"},"paging":{"pageSize":3}}"""),
            [],
            total: 6,
            pageNumbers: new Dictionary<string, int>(StringComparer.Ordinal) { ["page"] = 5 });

        Golden("past-the-end", html);
        Assert.Contains("Nothing to show yet.", html, StringComparison.Ordinal);
        Assert.Contains("<nav class=\"qb-pager\" aria-label=\"Pages\"><a class=\"qb-pager__step\" href=\"/site/blog\">Back to the first page</a></nav>", html, StringComparison.Ordinal);
    }
}
