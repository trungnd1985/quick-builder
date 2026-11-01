using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Cms.Items;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.IntegrationTests.Editor;

/// <summary>
/// Sessions end with their documents and with their holder's permission, lapsed rows and old
/// takeovers are deleted on every site, and live ones are kept (023 FR-019, research CE-03,
/// CE-12, CE-13). The cleanup's site-filter bypass is the one Principle V asks a test for.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EditingCleanupTests(DatabaseFixture database)
{
    [Fact]
    public async Task Deleting_a_document_or_its_collection_ends_its_sessions_and_handling_twice_changes_nothing()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "cleanup-deleted");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var page = await scenario.PageAsync("pricing");
        var component = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Footer")).GetProperty("id").GetGuid();
        var (item, collection, otherItem) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var onPage = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", page));
        var onComponent = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "component", component));
        var onItem = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "item", item, collection));
        var onOtherItem = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "item", otherItem, collection));
        await EditingApi.ClaimOkAsync(anna, scenario.SiteA, onPage);

        var now = DateTimeOffset.UtcNow;
        for (var twice = 0; twice < 2; twice++)
        {
            await scenario.App.DeliverAsync(new PageDeleted(Guid.NewGuid(), now, scenario.SiteA, page, "/pricing", null));
            await scenario.App.DeliverAsync(new ComponentDeleted(Guid.NewGuid(), now, scenario.SiteA, component, null));
            await scenario.App.DeliverAsync(new ItemDeleted(Guid.NewGuid(), now, scenario.SiteA, collection, item, false, null));
        }

        await PagesApi.RefusedAsync(await EditingApi.HeartbeatAsync(anna, scenario.SiteA, onPage), HttpStatusCode.NotFound, "editing.session_gone");
        await PagesApi.RefusedAsync(await EditingApi.HeartbeatAsync(anna, scenario.SiteA, onComponent), HttpStatusCode.NotFound, "editing.session_gone");
        await PagesApi.RefusedAsync(await EditingApi.HeartbeatAsync(anna, scenario.SiteA, onItem), HttpStatusCode.NotFound, "editing.session_gone");
        await EditingApi.HeartbeatOkAsync(anna, scenario.SiteA, onOtherItem);

        await scenario.App.DeliverAsync(new CollectionDeleted(Guid.NewGuid(), now, scenario.SiteA, collection, null));
        await PagesApi.RefusedAsync(await EditingApi.HeartbeatAsync(anna, scenario.SiteA, onOtherItem), HttpStatusCode.NotFound, "editing.session_gone");
    }

    [Fact]
    public async Task The_cleanup_deletes_lapsed_sessions_and_old_takeovers_on_every_site_and_keeps_the_rest()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "cleanup-service");
        var (anna, _) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var (bea, _) = await scenario.MemberAsync("Bea Lund", scenario.SiteB, SystemRole.Designer);
        var pageA = await scenario.PageAsync("pricing");
        var pageB = (await PagesApi.CreateOkAsync(scenario.Admin, scenario.SiteB, "about")).GetProperty("id").GetGuid();

        // Lapsed on both sites…
        await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", pageA);
        await EditingApi.OpenOkAsync(bea, scenario.SiteB, "page", pageB);
        scenario.Clock.Advance(TimeSpan.FromMinutes(3));

        // …and live on both.
        var liveA = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "page", pageA));
        var liveB = EditingApi.SessionId(await EditingApi.OpenOkAsync(bea, scenario.SiteB, "page", pageB));

        await AddTakeoversAsync(scenario, [(scenario.SiteA, 91), (scenario.SiteB, 91), (scenario.SiteA, 10), (scenario.SiteB, 10)]);

        await using (var scope = scenario.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EditingSessionCleanupService>().CleanUpAsync(CancellationToken.None);
        }

        var sessions = await scenario.ReadAsync(d => EditingScenario.Everything<EditingSession>(d).Select(s => s.Id).ToListAsync());
        Assert.Equal(new[] { liveA, liveB }.Order(), sessions.Order());
        var takeovers = await scenario.ReadAsync(d => EditingScenario.Everything<EditingTakeover>(d).ToListAsync());
        Assert.Equal(2, takeovers.Count);
        Assert.All(takeovers, t => Assert.True(scenario.Clock.GetUtcNow() - t.TakenAt < TimeSpan.FromDays(90)));
        Assert.Equal(new[] { scenario.SiteA, scenario.SiteB }.Order(), takeovers.Select(t => t.SiteId).Order());
    }

    [Fact]
    public async Task A_holder_who_loses_the_edit_capability_loses_the_lock_on_the_next_heartbeat()
    {
        using var scenario = await EditingScenario.CreateAsync(database, "cleanup-permission");
        var (anna, annaId) = await scenario.MemberAsync("Anna Tran", scenario.SiteA, SystemRole.Designer);
        var header = (await PagesApi.CreateComponentOkAsync(scenario.Admin, scenario.SiteA, "Header")).GetProperty("id").GetGuid();
        var session = EditingApi.SessionId(await EditingApi.OpenOkAsync(anna, scenario.SiteA, "component", header));
        await EditingApi.ClaimOkAsync(anna, scenario.SiteA, session);

        // Anna becomes an Editor, who may not change shared components.
        await using (var scope = scenario.App.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await identity.RoleGrants.Where(g => g.UserId == annaId).ExecuteUpdateAsync(s => s.SetProperty(g => g.Role, SystemRole.Editor));
        }

        var after = await EditingApi.HeartbeatOkAsync(anna, scenario.SiteA, session);
        Assert.Equal("view-only", after.GetProperty("access").GetString());
        Assert.Equal("viewer", EditingApi.Role(after));
        Assert.Null(EditingApi.Lock(after));
    }

    private static Task<int> AddTakeoversAsync(EditingScenario scenario, (Guid SiteId, int DaysAgo)[] takeovers) =>
        scenario.ReadAsync(async d =>
        {
            foreach (var (siteId, daysAgo) in takeovers)
            {
                var from = EditingSession.Open(siteId, "page", Guid.NewGuid(), null, Guid.NewGuid(), scenario.Clock.GetUtcNow());
                var at = scenario.Clock.GetUtcNow() - TimeSpan.FromDays(daysAgo);
                d.EditingTakeovers.Add(EditingTakeover.Record(from, Guid.NewGuid(), at - TimeSpan.FromMinutes(31), at));
            }

            return await d.SaveChangesAsync();
        });
}
