using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Features.Items.RunItemSchedule;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>A schedule runs exactly once (015 FR-026, SC-006; research IT-09), as 012's does for pages.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemScheduledExactlyOnceTests(DatabaseFixture database)
{
    [Fact]
    public async Task Three_dispatchers_at_once_produce_one_version()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-once-dispatchers");
        var id = await ScheduledAsync(scenario);

        await ItemScheduleTests.MakeDueAsync(scenario);
        await Task.WhenAll(ItemScheduleTests.DispatchAsync(scenario), ItemScheduleTests.DispatchAsync(scenario), ItemScheduleTests.DispatchAsync(scenario));

        Assert.Equal(1, await VersionsAsync(scenario, id));
        Assert.Equal(ItemScheduleStates.Done, await StateAsync(scenario, id));
    }

    [Fact]
    public async Task Delivering_the_message_again_or_an_old_revision_does_nothing()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-once-redelivery");
        var id = await ScheduledAsync(scenario);
        var schedule = await ScheduleAsync(scenario, id);

        await scenario.Cms.App.DeliverAsync(new ItemScheduleDue(Guid.NewGuid(), DateTimeOffset.UtcNow, scenario.Site, schedule.Id, schedule.Revision - 1));
        Assert.Equal(0, await VersionsAsync(scenario, id));

        var due = new ItemScheduleDue(Guid.NewGuid(), DateTimeOffset.UtcNow, scenario.Site, schedule.Id, schedule.Revision);
        await scenario.Cms.App.DeliverAsync(due);
        await scenario.Cms.App.DeliverAsync(due);
        Assert.Equal(1, await VersionsAsync(scenario, id));
    }

    [Fact]
    public async Task A_due_message_survives_a_stop_and_runs_once_after_start()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-once-restart");
        var id = await ScheduledAsync(scenario);

        // The message waits in the database while nothing runs; any instance started later picks it up.
        var waiting = await scenario.Cms.WithDatabaseAsync(cms => cms.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cms.outbox_messages WHERE event_type = 'Cms.ItemScheduleDue' AND completed_at_utc IS NULL")
            .SingleAsync());
        Assert.Equal(1, waiting);

        await ItemScheduleTests.MakeDueAsync(scenario);
        await ItemScheduleTests.DispatchAsync(scenario);
        await ItemScheduleTests.DispatchAsync(scenario);
        Assert.Equal(1, await VersionsAsync(scenario, id));
    }

    [Fact]
    public async Task A_schema_change_holding_the_collection_makes_the_run_wait_and_it_succeeds_afterwards()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-once-busy");
        var id = await ScheduledAsync(scenario);
        var schedule = await ScheduleAsync(scenario, id);
        var due = new ItemScheduleDue(Guid.NewGuid(), DateTimeOffset.UtcNow, scenario.Site, schedule.Id, schedule.Revision);

        await using (var scope = scenario.Cms.App.Services.CreateAsyncScope())
        {
            var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            await using var transaction = await cms.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<CollectionLocks>().LockForUpdateAsync(scenario.Site, scenario.Posts, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Cms.App.DeliverAsync(due));
            await transaction.RollbackAsync();
        }

        Assert.Equal(ItemScheduleStates.Pending, await StateAsync(scenario, id));
        await scenario.Cms.App.DeliverAsync(due);
        Assert.Equal(1, await VersionsAsync(scenario, id));
    }

    private static async Task<Guid> ScheduledAsync(ItemsScenario scenario)
    {
        var item = await scenario.PostAsync("once", new JsonObject { ["f1"] = "zz-canary-once" });
        await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(item), "publish", DateTimeOffset.UtcNow.AddHours(1), ItemsApi.Revision(item)));
        return ItemsApi.Id(item);
    }

    private static Task<ItemSchedule> ScheduleAsync(ItemsScenario scenario, Guid itemId) =>
        scenario.Cms.WithDatabaseAsync(cms => cms.ForSite<ItemSchedule>(scenario.Site).AsNoTracking().SingleAsync(s => s.ItemId == itemId));

    private static async Task<string> StateAsync(ItemsScenario scenario, Guid itemId) => (await ScheduleAsync(scenario, itemId)).State;

    private static Task<int> VersionsAsync(ItemsScenario scenario, Guid itemId) =>
        scenario.Cms.WithDatabaseAsync(cms => cms.ForSite<ItemVersion>(scenario.Site).CountAsync(v => v.ItemId == itemId));
}
