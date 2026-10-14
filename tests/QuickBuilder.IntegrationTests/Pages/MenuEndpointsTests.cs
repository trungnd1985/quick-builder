using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Pages.Domain.Menus;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Menus over HTTP, before anything is published (024 US1; FR-001, FR-002, FR-005 to FR-011,
/// FR-035; contracts/menus-api.yaml).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MenuEndpointsTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_menu_is_created_empty_and_names_are_unique_per_site_ignoring_case()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-create").ConfigureAwait(true);

        var response = await CreateAsync(world.Admin, world.Site, $"{PagesApi.CanaryMarker}Main").ConfigureAwait(true);
        var main = await PagesApi.OkAsync(response, HttpStatusCode.Created).ConfigureAwait(true);

        Assert.EndsWith($"/menus/{main.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Equal(0, main.GetProperty("entryCount").GetInt32());
        Assert.Equal("draft", main.GetProperty("publication").GetProperty("state").GetString());
        Assert.Empty(main.GetProperty("draft").GetProperty("entries").EnumerateArray());

        await PagesApi.RefusedAsync(
            await CreateAsync(world.Admin, world.Site, $"{PagesApi.CanaryMarker}MAIN").ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.duplicate_menu_name").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await CreateAsync(world.Admin, world.Site, "   ").ConfigureAwait(true),
            HttpStatusCode.BadRequest, "request.invalid").ConfigureAwait(true);

        // Another site may use the same name.
        await CreateOkAsync(world.Admin, world.Cms.SiteB, $"{PagesApi.CanaryMarker}Main").ConfigureAwait(true);

        var list = await PagesApi.OkAsync(await ListAsync(world.Admin, world.Site).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(50, list.GetProperty("limit").GetInt32());
        Assert.Single(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task The_fifty_first_menu_is_refused()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-limit").ConfigureAwait(true);

        for (var i = 0; i < Menu.MaxPerSite; i++)
        {
            await CreateOkAsync(world.Admin, world.Site, $"Menu {i}").ConfigureAwait(true);
        }

        var refused = await PagesApi.RefusedAsync(
            await CreateAsync(world.Admin, world.Site, "One too many").ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.menu_limit_reached").ConfigureAwait(true);
        Assert.Equal("50", refused.Text("limit"));
    }

    [Fact]
    public async Task A_draft_with_every_target_type_reads_back_unchanged()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-roundtrip").ConfigureAwait(true);
        var services = await world.PageAsync("services").ConfigureAwait(true);
        var hosting = await world.PageAsync("hosting", services).ConfigureAwait(true);
        var post = ItemsApiId(await world.PublishedPostAsync("hello-world").ConfigureAwait(true));
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);

        var document = Document(
            Entry("services", PageTarget(services), children:
                Entry("hosting", PageTarget(hosting, anchor: "plans"), children:
                    Entry("status", UrlTarget("https://status.example"), label: "Status", newTab: true))),
            Entry("latest", ItemTarget(world.Posts, post), label: "Latest post"),
            Entry("top", AnchorTarget("top"), label: "Back to top"),
            Entry("company", NoTarget(), label: "Company", hidden: true));

        var saved = await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, document).ConfigureAwait(true);
        var read = await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true);

        Assert.True(JsonEquals(JsonDocument.Parse(document).RootElement, read.GetProperty("draft")), read.GetProperty("draft").GetRawText());
        Assert.Equal(saved.GetProperty("draftVersion").GetUInt32(), read.GetProperty("draftVersion").GetUInt32());
        Assert.Equal(6, read.GetProperty("entryCount").GetInt32());

        var states = read.GetProperty("targets").EnumerateArray()
            .ToDictionary(t => t.GetProperty("id").GetGuid(), t => t.GetProperty("state").GetString());
        Assert.Equal("not_published", states[services]);
        Assert.Equal("not_published", states[hosting]);
        Assert.Equal("no_page", states[post]);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "entries": [ { "id": "a", "label": "A", "target": { "type": "none" }, "children": [ { "id": "b", "label": "B", "target": { "type": "none" }, "children": [ { "id": "c", "label": "C", "target": { "type": "none" }, "children": [ { "id": "d", "label": "D", "target": { "type": "none" }, "children": [] } ] } ] } ] } ] }""", "too_deep", "/entries/0/children/0/children/0/children/0")]
    [InlineData("""{ "schemaVersion": 1, "entries": [ { "id": "a", "target": { "type": "url", "href": "https://example.com" }, "children": [] } ] }""", "label_required", "/entries/0")]
    [InlineData("""{ "schemaVersion": 1, "entries": [ { "id": "a", "label": "X", "target": { "type": "url", "href": "javascript:alert(1)" }, "children": [] } ] }""", "entry_invalid", "/entries/0/target/href")]
    [InlineData("""{ "schemaVersion": 1, "entries": [ { "id": "a", "label": "X", "target": { "type": "none" }, "children": [] }, { "id": "a", "label": "Y", "target": { "type": "none" }, "children": [] } ] }""", "duplicate_entry_id", "/entries/1/id")]
    public async Task An_invalid_draft_is_refused_with_its_rule_and_pointer(string document, string rule, string location)
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-invalid").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);

        var refused = await PagesApi.RefusedAsync(
            await SaveCurrentDraftAsync(world.Admin, world.Site, menu, document).ConfigureAwait(true),
            HttpStatusCode.BadRequest, "pages.invalid_menu_document").ConfigureAwait(true);

        Assert.Equal((rule, location), (refused.Text("rule"), refused.Text("pointer")));
        Assert.Empty((await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("draft").GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task A_stale_version_is_refused_for_drafts_renames_and_deletes()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-stale").ConfigureAwait(true);
        var menu = await CreateOkAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        var id = menu.GetProperty("id").GetGuid();
        var draftVersion = menu.GetProperty("draftVersion").GetUInt32();
        var version = menu.GetProperty("version").GetUInt32();

        var afterSave = (await SaveCurrentDraftOkAsync(world.Admin, world.Site, id, Document(Entry("a", NoTarget(), label: "A"))).ConfigureAwait(true))
            .GetProperty("draftVersion").GetUInt32();
        await PagesApi.RefusedAsync(
            await SaveDraftAsync(world.Admin, world.Site, id, Document(), draftVersion).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);

        await PagesApi.OkAsync(await RenameAsync(world.Admin, world.Site, id, "Footer", version).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await RenameAsync(world.Admin, world.Site, id, "Other", version).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await DeleteAsync(world.Admin, world.Site, id, version).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.out_of_date").ConfigureAwait(true);

        // A rename never marks the draft as changed: the draft keeps its own version.
        Assert.Equal(afterSave, (await GetOkAsync(world.Admin, world.Site, id).ConfigureAwait(true)).GetProperty("draftVersion").GetUInt32());
    }

    [Fact]
    public async Task New_targets_must_belong_to_the_site_but_kept_broken_ones_do_not_block_saving()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-targets").ConfigureAwait(true);
        var about = await world.PageAsync("about").ConfigureAwait(true);
        var otherSitePage = (await PagesApi.CreateOkAsync(world.Admin, world.Cms.SiteB, "elsewhere").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var person = ItemsApiId(await world.PublishedPersonAsync("Ana").ConfigureAwait(true));
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);

        foreach (var (target, why) in new[]
                 {
                     (PageTarget(otherSitePage), "another site's page"),
                     (PageTarget(Guid.NewGuid()), "an unknown page"),
                     (ItemTarget(world.Posts, Guid.NewGuid()), "an unknown item"),
                     (ItemTarget(world.Posts, person), "an item of another collection"),
                 })
        {
            var refused = await PagesApi.RefusedAsync(
                await SaveCurrentDraftAsync(world.Admin, world.Site, menu, Document(Entry("e", target))).ConfigureAwait(true),
                HttpStatusCode.BadRequest, "pages.invalid_menu_document").ConfigureAwait(true);
            Assert.True(refused.Text("rule") == "unknown_target" && refused.Text("pointer") == "/entries/0/target", why);
        }

        // A target accepted once stays acceptable after its page is deleted.
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("about", PageTarget(about)))).ConfigureAwait(true);
        var aboutVersion = (await PagesApi.OkAsync(await PagesApi.GetAsync(world.Admin, world.Site, about).ConfigureAwait(true)).ConfigureAwait(true))
            .GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await PagesApi.DeleteAsync(world.Admin, world.Site, about, aboutVersion).ConfigureAwait(true)).StatusCode);

        var resaved = await SaveCurrentDraftOkAsync(
            world.Admin, world.Site, menu, Document(Entry("about", PageTarget(about)), Entry("more", NoTarget(), label: "More"))).ConfigureAwait(true);

        Assert.Equal("deleted", resaved.GetProperty("targets")[0].GetProperty("state").GetString());
        Assert.Equal(1, resaved.GetProperty("brokenCount").GetInt32());
    }

    [Fact]
    public async Task Duplicate_copies_the_draft_and_publishes_nothing()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-duplicate").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        var document = Document(Entry("a", UrlTarget("https://example.com"), label: "Example"));
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, document).ConfigureAwait(true);

        var copy = await CreateOkAsync(world.Admin, world.Site, "Main (copy)", duplicateOf: menu).ConfigureAwait(true);

        Assert.True(JsonEquals(JsonDocument.Parse(document).RootElement, copy.GetProperty("draft")));
        Assert.Equal("draft", copy.GetProperty("publication").GetProperty("state").GetString());

        await PagesApi.RefusedAsync(
            await CreateAsync(world.Admin, world.Site, "Copy of nothing", duplicateOf: Guid.NewGuid()).ConfigureAwait(true),
            HttpStatusCode.NotFound, "pages.menu_not_found").ConfigureAwait(true);
    }

    [Fact]
    public async Task Editors_can_read_menus_but_not_change_them_and_other_sites_see_nothing()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-roles").ConfigureAwait(true);
        var menu = await CreateOkAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        var id = menu.GetProperty("id").GetGuid();
        var editor = await world.Cms.UserAsync((world.Site, SystemRole.Editor)).ConfigureAwait(true);
        var designer = await world.Cms.UserAsync((world.Site, SystemRole.Designer)).ConfigureAwait(true);
        var outsider = await world.Cms.UserAsync((world.Cms.SiteB, SystemRole.Designer)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await ListAsync(editor, world.Site).ConfigureAwait(true)).StatusCode);
        var asEditor = await GetOkAsync(editor, world.Site, id).ConfigureAwait(true);
        Assert.False(asEditor.GetProperty("permissions").GetProperty("canManage").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(editor, world.Site, "Mine").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveCurrentDraftAsync(editor, world.Site, id, Document()).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RenameAsync(editor, world.Site, id, "X", menu.GetProperty("version").GetUInt32()).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync(editor, world.Site, id, menu.GetProperty("version").GetUInt32()).ConfigureAwait(true)).StatusCode);

        Assert.True((await GetOkAsync(designer, world.Site, id).ConfigureAwait(true)).GetProperty("permissions").GetProperty("canManage").GetBoolean());
        await CreateOkAsync(designer, world.Site, "Footer").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(outsider, world.Site, id).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(world.Admin, world.Cms.SiteB, id).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Every_write_is_audited_without_names_and_delete_removes_the_menu()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-audit").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, $"{PagesApi.CanaryMarker}Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("a", NoTarget(), label: $"{PagesApi.CanaryMarker}Label"))).ConfigureAwait(true);
        var renamed = await PagesApi.OkAsync(await RenameAsync(
            world.Admin, world.Site, menu, $"{PagesApi.CanaryMarker}Footer",
            (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        var copy = await CreateIdAsync(world.Admin, world.Site, $"{PagesApi.CanaryMarker}Copy").ConfigureAwait(true);
        _ = copy;
        await CreateOkAsync(world.Admin, world.Site, $"{PagesApi.CanaryMarker}Dup", duplicateOf: menu).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(world.Admin, world.Site, menu, renamed.GetProperty("version").GetUInt32()).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).StatusCode);

        await using var scope = world.App.Services.CreateAsyncScope();
        var pages = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        var changes = await pages.ForSite<MenuChange>(world.Site).Where(c => c.MenuId == menu).OrderBy(c => c.AtUtc).Select(c => c.Action).ToListAsync().ConfigureAwait(true);
        Assert.Equal([MenuChangeActions.Created, MenuChangeActions.DraftSaved, MenuChangeActions.Renamed, MenuChangeActions.Deleted], changes);
        Assert.Contains(await pages.ForSite<MenuChange>(world.Site).Select(c => c.Action).ToListAsync().ConfigureAwait(true), a => a == MenuChangeActions.Duplicated);
        Assert.False(await pages.ForSite<Menu>(world.Site).AnyAsync(m => m.Id == menu).ConfigureAwait(true));
        Assert.False(await pages.ForSite<MenuDraft>(world.Site).AnyAsync(d => d.MenuId == menu).ConfigureAwait(true));
    }

    private static Guid ItemsApiId(JsonElement item) => ItemsApi.Id(item);

    private static bool JsonEquals(JsonElement a, JsonElement b) => JsonElement.DeepEquals(a, b);
}
