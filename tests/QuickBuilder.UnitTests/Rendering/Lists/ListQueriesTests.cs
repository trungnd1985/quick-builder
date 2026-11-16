using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Rendering.Binding;
using QuickBuilder.UnitTests.Rendering.Binding;

namespace QuickBuilder.UnitTests.Rendering.Lists;

/// <summary>
/// A list block becomes a published-item query with every scope value resolved to a literal
/// (016 research TB-09; binding-expression.md §5; tasks T065).
/// </summary>
/// <remarks>
/// The world is the binding contract's <c>rich-image-link</c> case: its page item is a post of
/// <c>…c001</c>, whose <c>f12</c> references a person of <c>…c002</c>.
/// </remarks>
public sealed class ListQueriesTests
{
    private const string Posts = "0192f5a0-0000-7000-8000-00000000c001";
    private const string People = "0192f5a0-0000-7000-8000-00000000c002";

    private static readonly ResolveCase World = ResolveCase.Load("rich-image-link");

    private static PublishedItem PageItem => World.Inputs().Data!.PageItem!;

    private static IReadOnlyDictionary<Guid, CollectionSchemaInfo> Schemas => World.Inputs().Data!.Schemas;

    private static JsonElement List(string props) =>
        JsonElement.Parse("""{"id":"l1","type":"collection-list","props":""" + props + ""","children":[]}""");

    private static PlannedList Plan(string props, PublishedItem? pageItem = null, int pageNumber = 1) =>
        ListQueries.Plan(List(props), pageItem, Schemas, new Dictionary<string, int> { ["page"] = pageNumber });

    [Fact]
    public void A_list_shows_twelve_items_by_default_and_at_most_a_hundred()
    {
        var plain = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"}}""").Query!;
        var limited = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"},"limit":6}""").Query!;
        var huge = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"},"limit":5000}""").Query!;

        Assert.Equal((Guid.Parse(Posts), 0, 12), (plain.CollectionId, plain.Offset, plain.Count));
        Assert.Null(plain.ItemIds);
        Assert.Equal(6, limited.Count);
        Assert.Equal(100, huge.Count);
    }

    [Fact]
    public void Paging_takes_the_page_size_and_ignores_the_limit()
    {
        var third = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"},"limit":6,"paging":{"pageSize":10}}""", pageNumber: 3).Query!;

        Assert.Equal((20, 10), (third.Offset, third.Count));
    }

    [Fact]
    public void Filters_and_sorts_are_carried_over_and_scope_values_become_literals()
    {
        var query = Plan($$$"""
            {"source":{"collectionId":"{{{Posts}}}"},
             "filters":[{"field":"f1","op":"contains","value":"linen"},
                        {"item":"slug","op":"ne","value":{"current":true}},
                        {"field":"f12","op":"eq","value":{"field":"f12"}},
                        {"field":"f2","op":"notEmpty"}],
             "sort":[{"field":"f1","dir":"desc"},{"item":"firstPublishedAt","dir":"asc"}]}
            """, PageItem).Query!;

        Assert.Equal(4, query.Filters.Count);
        Assert.Equal(("f1", ListOperator.Contains, "linen"), (query.Filters[0].Target.FieldId, query.Filters[0].Operator, query.Filters[0].Value!.Value.GetString()));
        Assert.Equal((ListBuiltIn.Slug, ListOperator.Ne, PageItem.Slug), (query.Filters[1].Target.BuiltIn!.Value, query.Filters[1].Operator, query.Filters[1].Value!.Value.GetString()));
        Assert.Equal(
            BindingResolver.ReadValue(PageItem, "f12")!.Value.GetString(),
            query.Filters[2].Value!.Value.GetString());
        Assert.Null(query.Filters[3].Value);
        Assert.All(query.Filters, f => Assert.False(f.Broken));
        Assert.Equal([("f1", true), (null, false)], query.Sorts.Select(s => (s.Target.FieldId, s.Descending)));
    }

    [Fact]
    public void A_scope_value_that_cannot_be_read_matches_nothing()
    {
        var noPageItem = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"},"filters":[{"field":"f12","op":"eq","value":{"field":"f12"}}]}""").Query!;
        var unknownScope = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"},"filters":[{"field":"f12","op":"eq","value":{"field":"f12","scope":"other"}}]}""", PageItem).Query!;
        var currentOutsideTemplate = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"},"filters":[{"item":"slug","op":"ne","value":{"current":true}}]}""").Query!;
        var unknownOperator = Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"},"filters":[{"field":"f1","op":"like","value":"x"}]}""").Query!;

        Assert.True(noPageItem.Filters[0].Broken);
        Assert.True(unknownScope.Filters[0].Broken);
        Assert.True(currentOutsideTemplate.Filters[0].Broken);
        Assert.True(unknownOperator.Filters[0].Broken);
    }

    [Fact]
    public void A_list_without_a_usable_source_has_no_query_and_shows_its_empty_state()
    {
        Assert.Null(Plan("""{"source":{}}""").Query);
        Assert.Null(Plan("""{}""").Query);

        // A field source needs the page item, and a multi-reference field of it.
        Assert.Null(Plan("""{"source":{"field":"f12"}}""").Query);
        Assert.Null(Plan("""{"source":{"field":"f12"}}""", PageItem).Query);
    }

    [Fact]
    public void The_key_names_the_list_and_the_page_item_around_it()
    {
        Assert.Equal(ResolvedData.ListKey("l1", []), Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"}}""").Key);
        Assert.Equal(ResolvedData.ListKey("l1", [PageItem.Id]), Plan($$$"""{"source":{"collectionId":"{{{Posts}}}"}}""", PageItem).Key);
    }

    [Fact]
    public void Only_lists_outside_cards_are_top_level()
    {
        using var document = JsonDocument.Parse($$$"""
            {"root":{"id":"root","type":"root","props":{},"children":[
              {"id":"a","type":"collection-list","props":{"source":{"collectionId":"{{{Posts}}}"}},"children":[
                {"id":"a-card","type":"list-card","props":{},"children":[
                  {"id":"inner","type":"collection-list","props":{"source":{"collectionId":"{{{People}}}"}},"children":[]}]}]},
              {"id":"s","type":"section","props":{},"children":[
                {"id":"b","type":"collection-list","props":{"source":{"collectionId":"{{{People}}}"}},"children":[]}]}]}}
            """);

        var lists = ListQueries.TopLevel([document.RootElement], maxLists: 20);

        Assert.Equal(["a", "b"], lists.Select(l => l.GetProperty("id").GetString()));
        Assert.Single(ListQueries.TopLevel([document.RootElement], maxLists: 1));
    }
}
