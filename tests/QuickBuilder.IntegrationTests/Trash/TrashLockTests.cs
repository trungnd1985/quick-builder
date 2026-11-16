using System.Net;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.IntegrationTests.Editor;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Trash;

/// <summary>A delete waits for another member's active editing lock (021 FR-008, research TR-09).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrashLockTests(DatabaseFixture database)
{
    [Fact]
    public async Task Another_members_active_lock_refuses_the_delete_and_names_them()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "trash-lock-held");
        var page = await scenario.PageAsync("pricing");
        var (holder, _) = await scenario.MemberAsync("Holder", scenario.SiteA, SystemRole.Designer);
        var session = EditingApi.SessionId(await EditingApi.OpenOkAsync(holder, scenario.SiteA, EditingDocumentKinds.Page, page));
        await EditingApi.ClaimOkAsync(holder, scenario.SiteA, session);

        var refused = await PagesApi.RefusedAsync(
            await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, page, 1),
            HttpStatusCode.Conflict,
            "editing.lock_held");
        Assert.NotNull(refused);

        // Nothing changed: the page is still in the tree.
        Assert.Contains("/pricing", await PagesApi.PathsAsync(scenario.Admin, scenario.SiteA));
    }

    [Fact]
    public async Task A_members_own_lock_in_another_tab_does_not_refuse_the_delete()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "trash-lock-own");
        var page = await scenario.PageAsync("pricing");
        var session = EditingApi.SessionId(await EditingApi.OpenOkAsync(scenario.Admin, scenario.SiteA, EditingDocumentKinds.Page, page));
        await EditingApi.ClaimOkAsync(scenario.Admin, scenario.SiteA, session);

        await PagesApi.OkAsync(await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, page, 1));
    }

    [Fact]
    public async Task A_stale_lock_no_longer_refuses_the_delete()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "trash-lock-stale");
        var page = await scenario.PageAsync("pricing");
        var (holder, _) = await scenario.MemberAsync("Holder", scenario.SiteA, SystemRole.Designer);
        var session = EditingApi.SessionId(await EditingApi.OpenOkAsync(holder, scenario.SiteA, EditingDocumentKinds.Page, page));
        await EditingApi.ClaimOkAsync(holder, scenario.SiteA, session);

        // Open and heartbeating, but idle past the stale period (023 CE-05).
        for (var minutes = 0; minutes < 31; minutes++)
        {
            scenario.Clock.Advance(TimeSpan.FromMinutes(1));
            await EditingApi.HeartbeatOkAsync(holder, scenario.SiteA, session);
            await EditingApi.HeartbeatOkAsync(scenario.Admin, scenario.SiteA, EditingApi.SessionId(await EditingApi.OpenOkAsync(scenario.Admin, scenario.SiteA, EditingDocumentKinds.Page, page)));
        }

        await PagesApi.OkAsync(await PagesApi.DeleteAsync(scenario.Admin, scenario.SiteA, page, 1));
    }
}
