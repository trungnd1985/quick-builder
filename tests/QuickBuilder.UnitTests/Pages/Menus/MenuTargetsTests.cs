using QuickBuilder.Pages.Domain.Menus;

namespace QuickBuilder.UnitTests.Pages.Menus;

/// <summary>Which pages and items a menu document points at (024 research NV-04).</summary>
public sealed class MenuTargetsTests
{
    private static readonly Guid PageA = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10");
    private static readonly Guid PageB = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e11");
    private static readonly Guid Collection = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20");
    private static readonly Guid Item = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e21");

    private static readonly string Document = $$"""
        { "schemaVersion": 1, "entries": [
          { "id": "a", "target": { "type": "page", "pageId": "{{PageA}}" }, "children": [
            { "id": "b", "label": "B", "target": { "type": "url", "href": "https://example.com" }, "children": [
              { "id": "c", "target": { "type": "item", "collectionId": "{{Collection}}", "itemId": "{{Item}}" }, "children": [] } ] } ] },
          { "id": "d", "label": "D", "target": { "type": "anchor", "anchor": "top" }, "children": [] },
          { "id": "e", "target": { "type": "page", "pageId": "{{PageB}}", "anchor": "form" }, "children": [] },
          { "id": "f", "target": { "type": "page", "pageId": "{{PageA}}" }, "children": [] },
          { "id": "g", "label": "G", "target": { "type": "none" }, "children": [] } ] }
        """;

    [Fact]
    public void Page_and_item_targets_come_in_document_order_with_their_pointers()
    {
        var targets = MenuTargets.Extract(Document);

        Assert.Equal(
            [
                new MenuTargetRef(MenuTargetKinds.Page, PageA, null, "/entries/0/target"),
                new MenuTargetRef(MenuTargetKinds.Item, Item, Collection, "/entries/0/children/0/children/0/target"),
                new MenuTargetRef(MenuTargetKinds.Page, PageB, null, "/entries/2/target"),
                new MenuTargetRef(MenuTargetKinds.Page, PageA, null, "/entries/3/target"),
            ],
            targets);
    }

    [Fact]
    public void Distinct_targets_count_a_page_once()
    {
        Assert.Equal(3, MenuTargets.Distinct(MenuTargets.Extract(Document)).Count);
    }

    [Fact]
    public void Entries_are_counted_at_every_level()
    {
        Assert.Equal(7, MenuTargets.CountEntries(Document));
        Assert.Equal(0, MenuTargets.CountEntries("""{"schemaVersion":1,"entries":[]}"""));
    }
}
