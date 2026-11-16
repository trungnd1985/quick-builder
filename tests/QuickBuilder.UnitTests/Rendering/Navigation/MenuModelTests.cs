using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Rendering.Navigation;

namespace QuickBuilder.UnitTests.Rendering.Navigation;

/// <summary>
/// What visitors get of a menu: links, labels and pruning (024 FR-022 to FR-024, research NV-10;
/// public-navigation.md §2).
/// </summary>
public sealed class MenuModelTests
{
    private static readonly Guid Live = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10");
    private static readonly Guid Draft = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e11");
    private static readonly Guid Template = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e12");
    private static readonly Guid Unpublished = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e13");
    private static readonly Guid Posts = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20");
    private static readonly Guid Post = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e21");
    private static readonly Guid Untemplated = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e22");
    private static readonly Guid People = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e23");

    private static RenderHarness Harness(string basePath = "")
    {
        var harness = new RenderHarness { BasePath = basePath };
        harness.Links[Live] = new PageLinkTarget("/services", IsLive: true, LiveTitle: "Services");
        harness.Links[Draft] = new PageLinkTarget("/draft", IsLive: false);
        harness.Links[Template] = new PageLinkTarget("/blog/{slug}", IsLive: false);
        harness.Links[Unpublished] = new PageLinkTarget("/old", IsLive: false, LiveTitle: "Old services");
        harness.Items[Post] = Item(Post, Posts, "hello-world", "Hello world");
        harness.Items[Untemplated] = Item(Untemplated, People, "ana", "Ana");
        harness.Patterns[Posts] = "/blog/{slug}";
        return harness;
    }

    private static PublishedItem Item(Guid id, Guid collection, string slug, string name) =>
        new(id, collection, slug, name, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, JsonElement.Parse("{}"));

    private static List<ResolvedEntry> Resolve(string entries, RenderHarness? harness = null, int levels = 3)
    {
        harness ??= Harness();
        using var document = JsonDocument.Parse($$"""{ "schemaVersion": 1, "entries": [ {{entries}} ] }""");
        var inputs = harness.Inputs(RenderHarness.Document("[]"));
        return MenuModel.Prune(MenuModel.Resolve(document.RootElement, inputs), levels);
    }

    private static string PageEntry(string id, Guid page, string? label = null, string anchor = "", string children = "") =>
        $$"""{ "id": "{{id}}", {{(label is null ? "" : $"\"label\": \"{label}\",")}} "target": { "type": "page", "pageId": "{{page}}"{{(anchor.Length == 0 ? "" : $", \"anchor\": \"{anchor}\"")}} }, "children": [ {{children}} ] }""";

    [Fact]
    public void A_live_page_links_to_its_current_address_with_its_title_unless_the_entry_has_a_label()
    {
        var entries = Resolve($"{PageEntry("a", Live)}, {PageEntry("b", Live, "Our work", "pricing")}", Harness("/site"));

        Assert.Equal(("Services", "/site/services"), (entries[0].Label, entries[0].Href));
        Assert.Equal(("Our work", "/site/services#pricing"), (entries[1].Label, entries[1].Href));
    }

    [Fact]
    public void An_item_links_to_its_page_under_its_live_template_with_its_published_name()
    {
        var entries = Resolve($$"""
            { "id": "a", "target": { "type": "item", "collectionId": "{{Posts}}", "itemId": "{{Post}}" }, "children": [] },
            { "id": "b", "label": "Latest", "target": { "type": "item", "collectionId": "{{Posts}}", "itemId": "{{Post}}" }, "newTab": true, "children": [] }
            """);

        Assert.Equal(("Hello world", "/blog/hello-world", false), (entries[0].Label, entries[0].Href, entries[0].NewTab));
        Assert.Equal(("Latest", true), (entries[1].Label, entries[1].NewTab));
    }

    [Fact]
    public void Targets_that_are_not_live_are_left_out()
    {
        var entries = Resolve($$"""
            {{PageEntry("draft", Draft, "Draft")}},
            {{PageEntry("template", Template, "Template")}},
            {{PageEntry("missing", Guid.NewGuid(), "Missing")}},
            { "id": "unknown-item", "label": "Gone", "target": { "type": "item", "collectionId": "{{Posts}}", "itemId": "{{Guid.NewGuid()}}" }, "children": [] },
            { "id": "no-page", "target": { "type": "item", "collectionId": "{{People}}", "itemId": "{{Untemplated}}" }, "children": [] },
            {{PageEntry("live", Live)}}
            """);

        Assert.Equal(["live"], entries.Select(e => e.Id));
    }

    [Fact]
    public void A_parent_that_is_not_live_keeps_its_shown_children_under_its_label()
    {
        var entries = Resolve(PageEntry("old", Unpublished, children: PageEntry("child", Live)));

        var parent = Assert.Single(entries);
        Assert.Equal(("Old services", null), (parent.Label, parent.Href));
        Assert.Equal("child", Assert.Single(parent.Children).Id);
    }

    [Fact]
    public void Hidden_entries_go_with_their_children()
    {
        var entries = Resolve($$"""
            { "id": "h", "label": "Hidden", "target": { "type": "none" }, "hidden": true, "children": [ {{PageEntry("child", Live)}} ] },
            {{PageEntry("shown", Live)}}
            """);

        Assert.Equal(["shown"], entries.Select(e => e.Id));
    }

    [Fact]
    public void A_group_without_shown_children_is_left_out()
    {
        var entries = Resolve($$"""
            { "id": "empty", "label": "Empty", "target": { "type": "none" }, "children": [] },
            { "id": "dead", "label": "Dead", "target": { "type": "none" }, "children": [ {{PageEntry("draft", Draft, "Draft")}} ] },
            { "id": "ok", "label": "Ok", "target": { "type": "none" }, "children": [ {{PageEntry("child", Live)}} ] }
            """);

        Assert.Equal(["ok"], entries.Select(e => e.Id));
    }

    [Fact]
    public void Levels_beyond_the_blocks_are_dropped_and_their_groups_with_them()
    {
        var three = $$"""
            { "id": "l1", "label": "One", "target": { "type": "none" }, "children": [
              { "id": "l2", "label": "Two", "target": { "type": "none" }, "children": [ {{PageEntry("l3", Live)}} ] },
              {{PageEntry("l2-link", Live)}} ] }
            """;

        Assert.Equal(["l2", "l2-link"], Resolve(three, levels: 3)[0].Children.Select(c => c.Id));
        Assert.Equal(["l2-link"], Resolve(three, levels: 2)[0].Children.Select(c => c.Id));
        Assert.Empty(Resolve(three, levels: 1));
    }

    [Fact]
    public void Addresses_and_anchors_link_as_written_and_bad_anchors_do_not()
    {
        var entries = Resolve("""
            { "id": "url", "label": "Docs", "target": { "type": "url", "href": "https://docs.example/a?b=1" }, "newTab": true, "children": [] },
            { "id": "anchor", "label": "Top", "target": { "type": "anchor", "anchor": "top" }, "children": [] },
            { "id": "bad", "label": "Bad", "target": { "type": "anchor", "anchor": "1st\" onclick=\"x" }, "children": [] }
            """);

        Assert.Equal([("url", "https://docs.example/a?b=1", true), ("anchor", "#top", false)], entries.Select(e => (e.Id, e.Href!, e.External)));
        Assert.True(entries[0].NewTab);
    }

    [Fact]
    public void An_entry_with_nothing_to_call_it_is_left_out()
    {
        Assert.Empty(Resolve(PageEntry("nameless", Guid.NewGuid())));
    }

    [Fact]
    public void A_missing_or_empty_document_shows_nothing()
    {
        using var document = JsonDocument.Parse("{}");
        Assert.Empty(MenuModel.Resolve(document.RootElement, Harness().Inputs(RenderHarness.Document("[]"))));
        Assert.Empty(Resolve(string.Empty));
    }
}
