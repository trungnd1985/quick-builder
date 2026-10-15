using System.Net;
using System.Net.Http.Json;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.IntegrationTests.Templates;
using QuickBuilder.Shared.Kernel.Authorization;
using static QuickBuilder.IntegrationTests.Pages.MenusApi;

namespace QuickBuilder.IntegrationTests.Pages;

/// <summary>
/// Menus are published like components (024 FR-003, FR-003a, FR-004, FR-004a; research NV-01,
/// NV-05, NV-06): versions, history, rollback and schedules; nothing that shows an unpublished
/// menu goes live; a menu that live content shows cannot be taken away.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MenuPublishingTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_menu_is_published_versioned_rolled_back_and_its_name_is_the_published_one()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-publish").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("a", UrlTarget("https://one.example"), label: "One"))).ConfigureAwait(true);

        var first = await PublishOkAsync(world, menu).ConfigureAwait(true);
        Assert.Equal(("published", 1, false), (first.GetProperty("state").GetString(), first.GetProperty("liveNumber").GetInt32(), first.GetProperty("hasUnpublishedChanges").GetBoolean()));

        // A rename touches the menu, not its draft: no unpublished changes, and the live version keeps its name.
        var renamed = await PagesApi.OkAsync(await RenameAsync(world.Admin, world.Site, menu, "Primary",
            (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32()).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(renamed.GetProperty("publication").GetProperty("hasUnpublishedChanges").GetBoolean());
        Assert.Equal("Main", (await LiveMenuAsync(world, menu).ConfigureAwait(true))!.Name);

        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("a", UrlTarget("https://two.example"), label: "Two"))).ConfigureAwait(true);
        Assert.True((await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("publication").GetProperty("hasUnpublishedChanges").GetBoolean());
        await PublishOkAsync(world, menu).ConfigureAwait(true);
        Assert.Equal("Primary", (await LiveMenuAsync(world, menu).ConfigureAwait(true))!.Name);

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(world.Admin, MenuPath(world.Site, menu)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal([1, 2], history.GetProperty("retainedVersions").EnumerateArray().Select(v => v.GetInt32()).Order());
        Assert.Contains(history.GetProperty("entries").EnumerateArray(), e => e.GetProperty("subjectLabel").GetString() == "Primary");

        // Rollback restores version 1's entries and its name.
        await PagesApi.OkAsync(await PublishingApi.RollbackAsync(world.Admin, MenuPath(world.Site, menu), 1).ConfigureAwait(true)).ConfigureAwait(true);
        var live = (await LiveMenuAsync(world, menu).ConfigureAwait(true))!;
        Assert.Equal("Main", live.Name);
        Assert.Contains("one.example", live.Document.GetRawText(), StringComparison.Ordinal);

        var version = await PagesApi.OkAsync(await PublishingApi.VersionAsync(world.Admin, MenuPath(world.Site, menu), 2).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("two.example", version.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copying_a_version_into_the_draft_goes_through_the_menu_save()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-copy").ConfigureAwait(true);
        var about = await world.PageAsync("about").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(Entry("about", PageTarget(about)))).ConfigureAwait(true);
        await PublishOkAsync(world, menu).ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document()).ConfigureAwait(true);

        var draftVersion = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("draftVersion").GetUInt32();
        var copied = await PagesApi.OkAsync(await PublishingApi.CopyToDraftAsync(world.Admin, MenuPath(world.Site, menu), 1, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Single(copied.GetProperty("document").GetProperty("entries").EnumerateArray());
        var references = await PagesApi.OkAsync(await ReferencesAsync(world.Admin, world.Site, $"pageId={about}").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains(references.GetProperty("items").EnumerateArray(), r => r.GetProperty("menuId").GetGuid() == menu && r.GetProperty("inDraft").GetBoolean());
    }

    [Fact]
    public async Task A_scheduled_menu_publish_runs_but_menus_cannot_schedule_an_unpublish()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-schedule").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        var draftVersion = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("draftVersion").GetUInt32();

        var set = await PublishingApi.ScheduleAsync(world.Admin, MenuPath(world.Site, menu), "publish", DateTimeOffset.UtcNow.AddHours(1), draftVersion).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Single((await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("publication").GetProperty("pendingSchedules").EnumerateArray());

        var unpublish = await PublishingApi.ScheduleAsync(world.Admin, MenuPath(world.Site, menu), "unpublish", DateTimeOffset.UtcNow.AddHours(1), null).ConfigureAwait(true);
        Assert.True(unpublish.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{unpublish.StatusCode}");
    }

    [Fact]
    public async Task Nothing_that_shows_an_unpublished_or_deleted_menu_goes_live()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-guard-publish").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        var page = await world.PageAsync("home").ConfigureAwait(true);
        var pagePath = PagesApi.PagePath(world.Site, page);
        await PublishingApi.SaveAsync(world.Admin, pagePath, PageShowing(menu)).ConfigureAwait(true);

        var refused = await PagesApi.RefusedAsync(
            await PublishingApi.PublishAsync(world.Admin, pagePath, await PublishingApi.DraftVersionAsync(world.Admin, pagePath).ConfigureAwait(true)).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.unpublished_menus").ConfigureAwait(true);
        Assert.Contains(menu.ToString(), refused.Body.GetProperty("menuIds").GetRawText(), StringComparison.OrdinalIgnoreCase);

        // A component that shows it is refused alike.
        var header = (await PagesApi.CreateComponentOkAsync(world.Admin, world.Site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = PagesApi.ComponentPath(world.Site, header);
        await PublishingApi.SaveAsync(world.Admin, headerPath, ComponentShowing(menu)).ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PublishingApi.PublishAsync(world.Admin, headerPath, await PublishingApi.DraftVersionAsync(world.Admin, headerPath).ConfigureAwait(true)).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.unpublished_menus").ConfigureAwait(true);

        // Published, both go live; a scheduled page publish fails if the menu is deleted first.
        await PublishOkAsync(world, menu).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(world.Admin, pagePath).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(world.Admin, headerPath).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_rollback_to_a_version_showing_a_deleted_menu_is_refused()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-guard-rollback").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Old").ConfigureAwait(true);
        await PublishOkAsync(world, menu).ConfigureAwait(true);
        var page = await world.PageAsync("home").ConfigureAwait(true);
        var pagePath = PagesApi.PagePath(world.Site, page);
        await world.PublishDocumentAsync(page, PageShowing(menu)).ConfigureAwait(true);
        await world.PublishDocumentAsync(page, PublishingApi.HeadingDocument("No menu now")).ConfigureAwait(true);

        // Nothing live shows the menu any more, so it can be deleted.
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(world.Admin, world.Site, menu,
            (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32()).ConfigureAwait(true)).StatusCode);

        var refused = await PagesApi.RefusedAsync(
            await PublishingApi.RollbackAsync(world.Admin, pagePath, 1).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.version_missing_menus").ConfigureAwait(true);
        Assert.Contains(menu.ToString(), refused.Body.GetProperty("menuIds").GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_menu_that_live_content_shows_cannot_be_unpublished_or_deleted()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-guard-inuse").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await PublishOkAsync(world, menu).ConfigureAwait(true);

        var page = await world.PageAsync("home").ConfigureAwait(true);
        await world.PublishDocumentAsync(page, PageShowing(menu)).ConfigureAwait(true);
        var header = (await PagesApi.CreateComponentOkAsync(world.Admin, world.Site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = PagesApi.ComponentPath(world.Site, header);
        await PublishingApi.SaveAsync(world.Admin, headerPath, ComponentShowing(menu)).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(world.Admin, headerPath).ConfigureAwait(true);

        var unpublish = await PagesApi.RefusedAsync(
            await PublishingApi.UnpublishAsync(world.Admin, MenuPath(world.Site, menu)).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.menu_in_use_by_published_content").ConfigureAwait(true);
        Assert.Equal(("1", "1"), (unpublish.Text("pages"), unpublish.Text("components")));

        var version = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await PagesApi.RefusedAsync(
            await DeleteAsync(world.Admin, world.Site, menu, version).ConfigureAwait(true),
            HttpStatusCode.Conflict, "pages.menu_in_use_by_published_content").ConfigureAwait(true);

        var usages = await PagesApi.OkAsync(await UsagesAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(2, usages.GetProperty("items").GetArrayLength());
        Assert.All(usages.GetProperty("items").EnumerateArray(), u => Assert.True(u.GetProperty("inLive").GetBoolean()));
    }

    [Fact]
    public async Task A_menu_only_drafts_show_can_be_deleted_and_those_drafts_stay_saveable()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-guard-drafts").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        var page = await world.PageAsync("home").ConfigureAwait(true);
        var pagePath = PagesApi.PagePath(world.Site, page);
        await PublishingApi.SaveAsync(world.Admin, pagePath, PageShowing(menu)).ConfigureAwait(true);

        var usages = await PagesApi.OkAsync(await UsagesAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).ConfigureAwait(true);
        var only = Assert.Single(usages.GetProperty("items").EnumerateArray());
        Assert.Equal(("page", true, false), (only.GetProperty("kind").GetString(), only.GetProperty("inDraft").GetBoolean(), only.GetProperty("inLive").GetBoolean()));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(world.Admin, world.Site, menu,
            (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(true)).GetProperty("version").GetUInt32()).ConfigureAwait(true)).StatusCode);

        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(world.Admin, pagePath, PageShowing(menu, "\"levels\": 2")).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Fact]
    public async Task Editors_can_preview_menus_but_not_publish_them()
    {
        using var world = await TemplatesWorld.CreateAsync(database, "menu-editor-role").ConfigureAwait(true);
        var about = await world.PageAsync("about").ConfigureAwait(true);
        var menu = await CreateIdAsync(world.Admin, world.Site, "Main").ConfigureAwait(true);
        await SaveCurrentDraftOkAsync(world.Admin, world.Site, menu, Document(
            Entry("about", PageTarget(about), children: Entry("x", UrlTarget("https://x.example"), label: "X", newTab: true)),
            Entry("hid", NoTarget(), label: "Hidden", hidden: true))).ConfigureAwait(true);
        var editor = await world.Cms.UserAsync((world.Site, SystemRole.Editor)).ConfigureAwait(true);

        var preview = await PagesApi.OkAsync(await PreviewAsync(editor, world.Site, menu).ConfigureAwait(true)).ConfigureAwait(true);
        var first = preview.GetProperty("entries")[0];
        Assert.Equal((PagesApi.Title("about"), "page", "not_published"), (first.GetProperty("label").GetString(), first.GetProperty("kind").GetString(), first.GetProperty("state").GetString()));
        Assert.True(first.GetProperty("children")[0].GetProperty("newTab").GetBoolean());
        Assert.True(preview.GetProperty("entries")[1].GetProperty("hidden").GetBoolean());
        Assert.DoesNotContain("x.example", preview.GetRawText(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Forbidden, (await PublishingApi.PublishAsync(editor, MenuPath(world.Site, menu), 1).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(world.Admin, world.Cms.SiteB, menu).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UsagesAsync(world.Admin, world.Cms.SiteB, menu).ConfigureAwait(true)).StatusCode);
    }

    private static async Task<System.Text.Json.JsonElement> PublishOkAsync(TemplatesWorld world, Guid menu)
    {
        var draftVersion = (await GetOkAsync(world.Admin, world.Site, menu).ConfigureAwait(false)).GetProperty("draftVersion").GetUInt32();
        return await PagesApi.OkAsync(await PublishingApi.PublishAsync(world.Admin, MenuPath(world.Site, menu), draftVersion).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task<QuickBuilder.Pages.Contracts.Publishing.LiveMenu?> LiveMenuAsync(TemplatesWorld world, Guid menu)
    {
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(world.App.Services);
        // The public reader reads as the public path does: for the resolved site.
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<QuickBuilder.Shared.Infrastructure.MultiSite.ISiteContextSetter>(scope.ServiceProvider).SetSite(world.Site);
        var reader = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<QuickBuilder.Pages.Contracts.Publishing.IPublishedContentReader>(scope.ServiceProvider);
        return (await reader.GetLiveMenusAsync(world.Site, [menu]).ConfigureAwait(false)).GetValueOrDefault(menu);
    }

    private static string ComponentShowing(Guid menu) =>
        $$"""
        { "schemaVersion": 1, "root": { "id": "root", "type": "root", "props": {}, "children": [
          { "id": "s", "type": "section", "props": {}, "children": [
            { "id": "nav", "type": "navigation", "props": { "menuId": "{{menu}}" }, "children": [] } ] } ] } }
        """;
}
