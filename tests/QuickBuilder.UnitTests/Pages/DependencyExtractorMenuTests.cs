using QuickBuilder.Pages.Domain;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>What pages, components and menus depend on, for refreshes (024 research NV-07).</summary>
public sealed class DependencyExtractorMenuTests
{
    private static readonly Guid Main = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40");
    private static readonly Guid Footer = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e41");
    private static readonly Guid PageA = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10");
    private static readonly Guid Collection = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20");
    private static readonly Guid Item = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e21");

    [Fact]
    public void Each_navigation_block_is_a_menu_dependency()
    {
        var document = $$"""
            { "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [
              { "id": "s", "type": "section", "props": {}, "children": [
                { "id": "n1", "type": "navigation", "props": { "menuId": "{{Main}}" }, "children": [] },
                { "id": "c", "type": "container", "props": {}, "children": [
                  { "id": "n2", "type": "navigation", "props": { "menuId": "{{Footer}}" }, "children": [] },
                  { "id": "n3", "type": "navigation", "props": { "menuId": "{{Main}}" }, "children": [] } ] } ] } ] } }
            """;

        Assert.Equal([Main, Footer], DependencyExtractor.Menus(document).Order());
        Assert.Contains((DependencyKinds.Menu, Main), DependencyExtractor.Extract(document));
    }

    [Fact]
    public void A_menu_depends_on_its_pages_items_and_the_items_templates()
    {
        var menu = $$"""
            { "schemaVersion": 1, "entries": [
              { "id": "a", "target": { "type": "page", "pageId": "{{PageA}}", "anchor": "top" }, "children": [
                { "id": "b", "target": { "type": "item", "collectionId": "{{Collection}}", "itemId": "{{Item}}" }, "children": [] } ] },
              { "id": "c", "label": "C", "target": { "type": "url", "href": "https://example.com" }, "children": [] },
              { "id": "d", "target": { "type": "page", "pageId": "{{PageA}}" }, "children": [] } ] }
            """;

        Assert.Equal(
            new HashSet<(string, Guid)> { (DependencyKinds.Page, PageA), (DependencyKinds.Item, Item), (DependencyKinds.Template, Collection) },
            DependencyExtractor.MenuDependencies(menu).ToHashSet());
    }

    [Fact]
    public void An_empty_menu_depends_on_nothing()
    {
        Assert.Empty(DependencyExtractor.MenuDependencies("""{ "schemaVersion": 1, "entries": [] }"""));
    }
}
