using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>
/// A page or component moved to the trash comes back as it was (021 US1, SC-001, FR-001 to FR-005,
/// FR-014, FR-015).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PagesTrashRoundTripTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_deleted_page_goes_to_the_trash_and_comes_back_with_its_id_draft_and_address()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-page-rt").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, """
            about
              team
              history
            """).ConfigureAwait(true);
        var team = ids["/about/team"];
        var header = (await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        await PagesApi.OkAsync(await PagesApi.SaveCurrentDraftAsync(
            admin, PagesApi.PagePath(site, team), PagesApi.DocumentReferencing(header)).ConfigureAwait(true)).ConfigureAwait(true);
        var draftBefore = (await PagesApi.DraftOkAsync(admin, PagesApi.PagePath(site, team)).ConfigureAwait(true)).GetProperty("document").GetRawText();

        // FR-002: the receipt says until when it can be restored.
        var deleted = await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, team, 1).ConfigureAwait(true)).ConfigureAwait(true);
        var trashedAt = deleted.GetProperty("trashedAtUtc").GetDateTimeOffset();
        Assert.Equal(trashedAt.AddDays(30), deleted.GetProperty("purgeOnUtc").GetDateTimeOffset());

        // Gone from the tree; the row, its draft and the address it had are kept, the address is free.
        Assert.Equal(["/about", "/about/history"], await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
        Assert.Equal(1, await scenario.ReadAsync(db => Trashed<Page>(db).CountAsync(p => p.Id == team)).ConfigureAwait(true));
        Assert.Equal(1, await scenario.ReadAsync(db => PagesScenario.Everything<PageDraft>(db).CountAsync(d => d.PageId == team)).ConfigureAwait(true));
        Assert.Equal(0, await scenario.ReadAsync(db => PagesScenario.Everything<ComponentUsage>(db).CountAsync(u => u.PageId == team)).ConfigureAwait(true));
        Assert.Contains(await scenario.OutboxAsync().ConfigureAwait(true), e => e.Type == "Pages.PageDeleted");

        // FR-005: the trash lists it with who, when and where it was.
        var list = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        var entry = Assert.Single(list.GetProperty("entries").EnumerateArray());
        Assert.Equal(team, entry.GetProperty("id").GetGuid());
        Assert.Equal("/about/team", entry.GetProperty("address").GetString());
        Assert.Equal("live", entry.GetProperty("place").GetProperty("parentState").GetString());
        Assert.Equal(ids["/about"], entry.GetProperty("place").GetProperty("parentId").GetGuid());

        // The single entry, with its draft, for the preview (FR-011).
        var detail = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{team}", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(team, detail.GetProperty("entry").GetProperty("id").GetGuid());

        // Restore: the plan first, then confirm what the member saw.
        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{team}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("/about/team", plan.GetProperty("address").GetString());
        Assert.False(plan.GetProperty("addressChanged").GetBoolean());
        Assert.False(plan.GetProperty("canRestoreAndPublish").GetBoolean());

        await PagesApi.OkAsync(await RestoreAsync(admin, site, "pages", team, "/about/team").ConfigureAwait(true)).ConfigureAwait(true);

        // Same id, same place and position, same draft; its component use is back (TR-06).
        Assert.Equal(["/about", "/about/team", "/about/history"], await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
        Assert.Equal(team, (await PagesApi.NodeAsync(admin, site, "/about/team").ConfigureAwait(true)).GetProperty("id").GetGuid());
        var draftAfter = (await PagesApi.DraftOkAsync(admin, PagesApi.PagePath(site, team)).ConfigureAwait(true)).GetProperty("document").GetRawText();
        Assert.Equal(draftBefore, draftAfter);
        Assert.Equal(1, await scenario.ReadAsync(db => PagesScenario.Everything<ComponentUsage>(db).CountAsync(u => u.PageId == team && u.ComponentId == header)).ConfigureAwait(true));
        Assert.Contains(await scenario.OutboxAsync().ConfigureAwait(true), e => e.Type == "Pages.PageRestored");

        // It is out of the trash; a second restore is told so (FR-020).
        await PagesApi.RefusedAsync(
            await RestoreAsync(admin, site, "pages", team, "/about/team").ConfigureAwait(true),
            HttpStatusCode.NotFound, TrashErrorCodes.NotInTrash).ConfigureAwait(true);
    }

    [Fact]
    public async Task A_trashed_page_frees_its_address_and_comes_back_under_a_numbered_one()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-page-addr").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, """
            home*
            pricing
            """).ConfigureAwait(true);

        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, ids["/pricing"], 1).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.CreateOkAsync(admin, site, "pricing").ConfigureAwait(true);

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{ids["/pricing"]}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("/pricing-2", plan.GetProperty("address").GetString());
        Assert.True(plan.GetProperty("addressChanged").GetBoolean());

        // Confirming the address the member did not see is refused with the current plan.
        var changed = await PagesApi.RefusedAsync(
            await RestoreAsync(admin, site, "pages", ids["/pricing"], "/pricing").ConfigureAwait(true),
            HttpStatusCode.Conflict, TrashErrorCodes.PlanChanged).ConfigureAwait(true);
        Assert.NotNull(changed);

        await PagesApi.OkAsync(await RestoreAsync(admin, site, "pages", ids["/pricing"], "/pricing-2").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Contains("/pricing-2", await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
    }

    [Fact]
    public async Task The_only_page_comes_back_as_the_home_page()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-page-home").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var ids = await PagesApi.SeedTreeAsync(admin, site, "home*").ConfigureAwait(true);

        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, ids["/"], 1).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{ids["/"]}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.True(plan.GetProperty("becomesHome").GetBoolean());

        await PagesApi.OkAsync(await RestoreAsync(admin, site, "pages", ids["/"], "/").ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(["/"], await PagesApi.PathsAsync(admin, site).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_deleted_component_frees_its_name_and_comes_back_under_a_numbered_one()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-comp-rt").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var header = (await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, header).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetUInt32();

        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, header, version).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, (await PagesApi.GetComponentAsync(admin, site, header).ConfigureAwait(true)).StatusCode);

        // FR-004: the name is free while it is in the trash.
        await PagesApi.CreateComponentOkAsync(admin, site, "Header").ConfigureAwait(true);

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/components/{header}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        var name = plan.GetProperty("address").GetString()!;
        Assert.NotEqual("Header", name);
        Assert.True(plan.GetProperty("addressChanged").GetBoolean());

        await PagesApi.OkAsync(await RestoreAsync(admin, site, "components", header, name).ConfigureAwait(true)).ConfigureAwait(true);
        var back = await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, header).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(name, back.GetProperty("name").GetString());
        Assert.Contains(await scenario.OutboxAsync().ConfigureAwait(true), e => e.Type == "Pages.ComponentRestored");
    }

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, Guid site, string kind, Guid id, string confirmedAddress, bool publish = false) =>
        client.PostAsJsonAsync(
            new Uri($"/api/v1/pages/sites/{site}/trash/{kind}/{id}/restore", UriKind.Relative),
            new { placement = (string?)null, confirmedAddress, publish });

    private static IQueryable<T> Trashed<T>(QuickBuilder.Pages.Infrastructure.PagesDbContext db)
        where T : class, QuickBuilder.Shared.Kernel.MultiSite.ITrashable =>
        PagesScenario.Everything<T>(db).IncludingTrash("Test: the row of a trashed document is kept.");

    /// <summary>T021: a pending schedule is cancelled by the delete, reported, and not restored (021 FR-015).</summary>
    [Fact]
    public async Task A_page_with_a_pending_schedule_says_so_and_cannot_come_back_published()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-page-sched").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var page = (await PagesApi.SeedTreeAsync(admin, site, "launch").ConfigureAwait(true))["/launch"];
        var path = PagesApi.PagePath(site, page);
        await PublishingApi.SaveAsync(admin, path, PublishingApi.HeadingDocument("Launch")).ConfigureAwait(true);
        var draftVersion = await PublishingApi.DraftVersionAsync(admin, path).ConfigureAwait(true);
        (await PublishingApi.ScheduleAsync(admin, path, "publish", DateTimeOffset.UtcNow.AddDays(3), draftVersion).ConfigureAwait(true)).EnsureSuccessStatusCode();

        await PagesApi.OkAsync(await PagesApi.DeleteAsync(admin, site, page, 1).ConfigureAwait(true)).ConfigureAwait(true);

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"/api/v1/pages/sites/{site}/trash/pages/{page}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.True(plan.GetProperty("hadSchedule").GetBoolean());
        Assert.False(plan.GetProperty("canRestoreAndPublish").GetBoolean());
    }

    /// <summary>021 FR-014: a component that was live can come back live, in one confirmed step.</summary>
    [Fact]
    public async Task A_live_component_can_come_back_published_and_a_stale_plan_is_refused()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-comp-live").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var banner = (await PagesApi.CreateComponentOkAsync(admin, site, "Banner").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var path = PagesApi.ComponentPath(site, banner);
        await PublishingApi.SaveAsync(admin, path, PublishingApi.ComponentDocument("Live words")).ConfigureAwait(true);
        await PublishingApi.PublishCurrentAsync(admin, path).ConfigureAwait(true);
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, banner).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, banner, version).ConfigureAwait(true)).ConfigureAwait(true);
        var entry = $"/api/v1/pages/sites/{site}/trash/components/{banner}";

        var plan = await PagesApi.OkAsync(await admin.GetAsync(new Uri($"{entry}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.True(plan.GetProperty("wasLive").GetBoolean());
        Assert.True(plan.GetProperty("canRestoreAndPublish").GetBoolean());

        await PagesApi.RefusedAsync(
            await admin.PostAsJsonAsync(new Uri($"{entry}/restore", UriKind.Relative), new { confirmedAddress = "Not what was shown" }).ConfigureAwait(true),
            HttpStatusCode.Conflict, TrashErrorCodes.PlanChanged).ConfigureAwait(true);

        var restored = await PagesApi.OkAsync(await admin.PostAsJsonAsync(new Uri($"{entry}/restore", UriKind.Relative), new { confirmedAddress = "Banner", publish = true }).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.True(restored.GetProperty("published").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.GetComponentAsync(admin, site, banner).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>021 FR-019: a bulk restore applies each entry's own capability.</summary>
    [Fact]
    public async Task A_bulk_restore_handles_components_and_refuses_an_entry_the_member_may_not_restore()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-bulk-comp").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var banner = (await PagesApi.CreateComponentOkAsync(admin, site, "Banner").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, banner).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, banner, version).ConfigureAwait(true)).ConfigureAwait(true);
        var path = new Uri($"/api/v1/pages/sites/{site}/trash/restore", UriKind.Relative);
        var body = new { entries = new[] { new { kind = "component", id = banner, confirmedAddress = "Banner" } } };
        var editor = await scenario.UserAsync((site, SystemRole.Editor)).ConfigureAwait(true);

        var refused = await PagesApi.OkAsync(await editor.PostAsJsonAsync(path, body).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(refused.GetProperty("succeeded").EnumerateArray());
        Assert.Equal(banner, Assert.Single(refused.GetProperty("failed").EnumerateArray()).GetProperty("id").GetGuid());

        var outcome = await PagesApi.OkAsync(await admin.PostAsJsonAsync(path, body).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(banner, Assert.Single(outcome.GetProperty("succeeded").EnumerateArray()).GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await PagesApi.GetComponentAsync(admin, site, banner).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>TR-11: restoring and publishing are one step, so when publishing fails nothing is restored.</summary>
    [Fact]
    public async Task A_component_that_was_never_live_cannot_be_restored_and_published_and_stays_in_the_trash()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-comp-atomic").ConfigureAwait(true);
        var (admin, site) = (scenario.Admin, scenario.SiteA);
        var banner = (await PagesApi.CreateComponentOkAsync(admin, site, "Banner").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var version = (await PagesApi.OkAsync(await PagesApi.GetComponentAsync(admin, site, banner).ConfigureAwait(true)).ConfigureAwait(true)).GetProperty("version").GetUInt32();
        await PagesApi.OkAsync(await PagesApi.DeleteComponentAsync(admin, site, banner, version).ConfigureAwait(true)).ConfigureAwait(true);
        var entry = $"/api/v1/pages/sites/{site}/trash/components/{banner}";

        var refused = await admin.PostAsJsonAsync(new Uri($"{entry}/restore", UriKind.Relative), new { confirmedAddress = "Banner", publish = true }).ConfigureAwait(true);

        Assert.False(refused.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PagesApi.GetComponentAsync(admin, site, banner).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(new Uri(entry, UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_component_restore_on_an_unknown_site_is_not_found()
    {
        using var scenario = await PagesScenario.CreateAsync(database, "trash-comp-site").ConfigureAwait(true);
        var entry = $"/api/v1/pages/sites/{Guid.NewGuid()}/trash/components/{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync(new Uri($"{entry}/restore-plan", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.PostAsJsonAsync(new Uri($"{entry}/restore", UriKind.Relative), new { confirmedAddress = "Banner" }).ConfigureAwait(true)).StatusCode);
    }
}
