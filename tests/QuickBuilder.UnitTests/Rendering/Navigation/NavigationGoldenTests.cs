using System.Text.Json;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Rendering.Rendering.Navigation;

namespace QuickBuilder.UnitTests.Rendering.Navigation;

/// <summary>
/// The Navigation block's HTML (024 FR-017 to FR-020, SC-007; public-navigation.md §4). The
/// whole-page golden files of the page-schema examples run in <see cref="RendererGoldenTests"/>;
/// these pin the parts of the markup each property and state decides.
/// </summary>
public sealed class NavigationGoldenTests
{
    private static readonly Guid Menu = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e40");
    private static readonly Guid Other = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e41");
    private static readonly Guid Page = Guid.Parse("0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10");

    private static string Render(string menuEntries, string props = "", string name = "Main", string secondBlock = "")
    {
        var harness = new RenderHarness();
        harness.Links[Page] = new PageLinkTarget("/services", IsLive: true, LiveTitle: "Services");
        harness.Menus[Menu] = new LiveMenu(1, JsonDocument.Parse($$"""{ "schemaVersion": 1, "entries": [ {{menuEntries}} ] }""").RootElement.Clone(), name);
        harness.Menus[Other] = new LiveMenu(1, JsonDocument.Parse($$"""{ "schemaVersion": 1, "entries": [ {{Sub}} ] }""").RootElement.Clone(), "Footer");
        var blocks = $$"""{ "id": "nav", "type": "navigation", "props": { "menuId": "{{Menu}}"{{(props.Length == 0 ? "" : ", " + props)}} }, "children": [] }""";
        if (secondBlock.Length > 0)
        {
            blocks += ", " + secondBlock;
        }

        return harness.Page(RenderHarness.Document($$"""[{ "id": "s", "type": "section", "props": {}, "children": [ {{blocks}} ] }]"""));
    }

    private static readonly string Sub = $$"""
        { "id": "group", "label": "Group", "target": { "type": "none" }, "children": [
          { "id": "child", "target": { "type": "page", "pageId": "{{Page}}" }, "children": [] } ] }
        """;

    private static string Body(string html) => html[html.IndexOf("<nav", StringComparison.Ordinal)..(html.LastIndexOf("</nav>", StringComparison.Ordinal) + 6)];

    [Fact]
    public void The_defaults_are_a_horizontal_start_aligned_menu_named_after_the_menu()
    {
        var html = Render(Sub);

        Assert.Contains("<nav class=\"qb-nav qb-nav--horizontal qb-nav--align-start\" aria-label=\"Main\" data-qb-nav=\"n1\" data-qb=\"p.nav\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("qb-nav__panel", Body(html), StringComparison.Ordinal);
        Assert.Contains("[data-qb=\"p.nav\"]{--qb-nav-color:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_alignment_label_and_collapse_follow_the_props()
    {
        var html = Render(Sub, """ "layout": "vertical", "align": "end", "ariaLabel": "Site", "collapse": { "enabled": true, "below": "tablet", "buttonLabel": "Browse" } """);

        Assert.Contains("class=\"qb-nav qb-nav--vertical qb-nav--align-end qb-nav--collapse-tablet\" aria-label=\"Site\"", html, StringComparison.Ordinal);
        Assert.Contains("data-qb-collapse=\"tablet\"", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"qb-nav__panel\" open><summary class=\"qb-nav__toggle\">Browse</summary>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_menu_button_defaults_to_the_sites_language()
    {
        var html = Render(Sub, """ "collapse": { "enabled": true, "below": "mobile" } """);

        Assert.Contains("<summary class=\"qb-nav__toggle\">Menu</summary>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_linked_entry_with_children_has_its_own_button_and_a_group_uses_its_label()
    {
        var html = Body(Render($$"""
            { "id": "a", "target": { "type": "page", "pageId": "{{Page}}" }, "children": [
              { "id": "b", "label": "Docs", "target": { "type": "url", "href": "https://docs.example/" }, "children": [] } ] },
            {{Sub}}
            """));

        Assert.Contains("<a class=\"qb-nav__link\" href=\"/services\">Services</a><details class=\"qb-nav__sub\" name=\"qb-n1-l1\"><summary class=\"qb-nav__expander\"><span class=\"qb-nav__hidden\">Show submenu: Services</span></summary>", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"qb-nav__sub\" name=\"qb-n1-l1\"><summary class=\"qb-nav__expander qb-nav__expander--label\">Group</summary>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_tab_link_protects_the_page_and_says_so()
    {
        var html = Body(Render("""{ "id": "a", "label": "Docs", "target": { "type": "url", "href": "https://docs.example/" }, "newTab": true, "children": [] }"""));

        Assert.Contains("<a class=\"qb-nav__link\" href=\"https://docs.example/\" target=\"_blank\" rel=\"noopener noreferrer\">Docs<span class=\"qb-nav__hidden\"> (opens in a new tab)</span></a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_blocks_keep_their_submenu_groups_apart_and_the_script_comes_once()
    {
        var html = Render(Sub, secondBlock: $$"""{ "id": "nav2", "type": "navigation", "props": { "menuId": "{{Other}}" }, "children": [] }""");

        Assert.Contains("name=\"qb-n1-l1\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"qb-n2-l1\"", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split(NavScript.Element).Length - 1);
        Assert.True(html.IndexOf(NavScript.Element, StringComparison.Ordinal) > html.IndexOf("</nav>", StringComparison.Ordinal));
    }

    [Fact]
    public void A_menu_that_is_missing_not_live_or_empty_writes_nothing()
    {
        var harness = new RenderHarness();
        var missing = harness.Page(RenderHarness.Document($$"""[{ "id": "s", "type": "section", "props": {}, "children": [ { "id": "nav", "type": "navigation", "props": { "menuId": "{{Guid.NewGuid()}}" }, "children": [] } ] }]"""));
        var empty = Render("""{ "id": "a", "label": "Hidden", "target": { "type": "none" }, "hidden": true, "children": [] }""");

        Assert.DoesNotContain("<nav", missing, StringComparison.Ordinal);
        Assert.DoesNotContain("<nav", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Hostile_labels_and_names_are_encoded_everywhere()
    {
        const string Hostile = "\\\"><script>x</script>";
        var html = Render(
            $$"""
            { "id": "a", "label": "{{Hostile}}", "target": { "type": "none" }, "children": [
              { "id": "b", "label": "{{Hostile}}", "target": { "type": "url", "href": "https://docs.example/" }, "newTab": true, "children": [] } ] }
            """,
            name: "\"><img src=x>");

        Assert.DoesNotContain("<script>x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("&quot;&gt;&lt;script&gt;x&lt;/script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Disallowed_addresses_never_become_links()
    {
        var harness = new RenderHarness();
        harness.Menus[Menu] = new LiveMenu(1, JsonDocument.Parse("""{ "schemaVersion": 1, "entries": [ { "id": "a", "label": "Bad", "target": { "type": "url", "href": "javascript:alert(1)" }, "children": [] } ] }""").RootElement.Clone(), "Main");

        var html = harness.Page(RenderHarness.Document($$"""[{ "id": "s", "type": "section", "props": {}, "children": [ { "id": "nav", "type": "navigation", "props": { "menuId": "{{Menu}}" }, "children": [] } ] }]"""));

        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"qb-nav__text\">Bad</span>", html, StringComparison.Ordinal);
    }
}
