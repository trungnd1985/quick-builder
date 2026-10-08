using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Shared.Infrastructure.Outbox;

namespace QuickBuilder.IntegrationTests.Cms.Items;

/// <summary>
/// Scheduled publishes and unpublishes (015 US6, FR-024 to FR-027, SC-006; research IT-09). The
/// outbox is driven by hand, and a message is made due by moving its <c>available_at_utc</c>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemScheduleTests(DatabaseFixture database)
{
    private static readonly DateTimeOffset InAnHour = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public async Task A_scheduled_publish_publishes_what_was_captured_not_the_later_draft()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-capture");
        var item = await scenario.PostAsync("launch", new JsonObject { ["f1"] = "zz-canary-reviewed" });
        var id = ItemsApi.Id(item);

        item = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour, ItemsApi.Revision(item)));
        Assert.Equal("publish", item.GetProperty("schedules")[0].GetProperty("action").GetString());
        item = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("launch", new JsonObject { ["f1"] = "zz-canary-written-later" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)));
        Assert.True(item.GetProperty("scheduledDiffersFromDraft").GetBoolean());

        await MakeDueAsync(scenario);
        await DispatchAsync(scenario);

        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal("zz-canary-reviewed", ItemsApi.Values(after, "published")["f1"]!.GetValue<string>());
        Assert.Equal("changed", after.GetProperty("status").GetString());
        Assert.Equal(0, after.GetProperty("schedules").GetArrayLength());

        var history = await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, id));
        Assert.Equal("scheduled", history.GetProperty("versions")[0].GetProperty("kind").GetString());
        var published = history.GetProperty("events")[0];
        Assert.Equal(("published", "publish"), (published.GetProperty("kind").GetString(), published.GetProperty("scheduleAction").GetString()));
    }

    [Fact]
    public async Task Schedules_are_refused_in_the_past_out_of_order_or_with_nothing_to_unpublish()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-rules");
        var item = await scenario.PostAsync("rules");
        var id = ItemsApi.Id(item);

        Assert.Equal("cms.schedule_in_past", (await PagesApi.ProblemAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", DateTimeOffset.UtcNow.AddMinutes(-1), ItemsApi.Revision(item)))).Code);
        Assert.Equal("cms.item_not_published", (await PagesApi.ProblemAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "unpublish", InAnHour, ItemsApi.Revision(item)))).Code);

        item = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour, ItemsApi.Revision(item)));
        Assert.Equal("cms.schedule_order", (await PagesApi.ProblemAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "unpublish", InAnHour.AddMinutes(-5), ItemsApi.Revision(item)))).Code);
        item = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "unpublish", InAnHour.AddHours(1), ItemsApi.Revision(item)));
        Assert.Equal(2, item.GetProperty("schedules").GetArrayLength());

        // A draft that needs attention cannot be scheduled to publish.
        var incomplete = await scenario.PostAsync("incomplete", new JsonObject());
        Assert.Equal("cms.item_needs_attention", (await PagesApi.ProblemAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, ItemsApi.Id(incomplete), "publish", InAnHour, ItemsApi.Revision(incomplete)))).Code);
    }

    [Fact]
    public async Task Moving_a_schedule_makes_the_old_message_a_no_op_and_recapture_takes_the_current_draft()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-move");
        var item = await scenario.PostAsync("moved", new JsonObject { ["f1"] = "zz-canary-first" });
        var id = ItemsApi.Id(item);
        item = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour, ItemsApi.Revision(item)));
        item = await ItemsApi.SaveOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id,
            ItemsApi.Draft("moved", new JsonObject { ["f1"] = "zz-canary-second" }, scenario.PostsSchemaRevision, ItemsApi.Revision(item)));
        item = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour.AddHours(1), ItemsApi.Revision(item), recapture: true));
        Assert.False(item.GetProperty("scheduledDiffersFromDraft").GetBoolean());

        await MakeDueAsync(scenario);
        await DispatchAsync(scenario);

        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal("zz-canary-second", ItemsApi.Values(after, "published")["f1"]!.GetValue<string>());
        Assert.Equal(1, after.GetProperty("liveVersion").GetInt32());
        var kinds = (await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, id)))
            .GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["published", "rescheduled", "scheduled", "created"], kinds);
    }

    [Fact]
    public async Task A_manual_publish_needs_confirmation_to_cancel_a_pending_scheduled_publish()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-manual");
        var item = await scenario.PostAsync("manual");
        var id = ItemsApi.Id(item);
        item = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour, ItemsApi.Revision(item)));

        var refused = await PagesApi.ProblemAsync(await ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item)));
        Assert.Equal("cms.schedule_pending", refused.Code);
        Assert.Equal("publish", refused.Body.GetProperty("schedule").GetProperty("action").GetString());

        item = await PagesApi.OkAsync(await ItemsApi.PublishAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item), cancelSchedule: true));
        Assert.Equal(0, item.GetProperty("schedules").GetArrayLength());
        var kinds = (await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, id)))
            .GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        Assert.Equal(["published", "scheduleCancelled", "scheduled", "created"], kinds);

        // The cancelled schedule's message, delivered anyway, does nothing.
        await MakeDueAsync(scenario);
        await DispatchAsync(scenario);
        Assert.Equal(1, (await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id)).GetProperty("liveVersion").GetInt32());
    }

    [Fact]
    public async Task Cancelling_needs_a_pending_schedule_and_the_current_revision()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-cancel");
        var item = await scenario.PostAsync("cancel");
        var id = ItemsApi.Id(item);
        Assert.Equal("cms.schedule_not_found", (await PagesApi.ProblemAsync(await ItemsApi.CancelScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", ItemsApi.Revision(item)))).Code);

        var scheduled = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour, ItemsApi.Revision(item)));
        Assert.Equal("cms.item_conflict", (await PagesApi.ProblemAsync(await ItemsApi.CancelScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", ItemsApi.Revision(item)))).Code);

        var cancelled = await PagesApi.OkAsync(await ItemsApi.CancelScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", ItemsApi.Revision(scheduled)));
        Assert.Equal(0, cancelled.GetProperty("schedules").GetArrayLength());
    }

    [Fact]
    public async Task A_scheduled_unpublish_runs_and_a_manual_unpublish_cancels_it()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-unpublish");
        var item = await scenario.PostAsync("banner");
        var id = ItemsApi.Id(item);
        item = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(item));
        item = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "unpublish", InAnHour, ItemsApi.Revision(item)));

        await MakeDueAsync(scenario);
        await DispatchAsync(scenario);

        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal("draft", after.GetProperty("status").GetString());
        var unpublished = (await scenario.Cms.OutboxAsync()).Single(e => e.Type == "Cms.ItemUnpublished");
        Assert.True(JsonDocument.Parse(unpublished.Payload).RootElement.GetProperty("Scheduled").GetBoolean());

        // Publish again, schedule another unpublish, then unpublish by hand: the schedule goes.
        after = await ItemsApi.PublishOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(after));
        after = await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "unpublish", InAnHour, ItemsApi.Revision(after)));
        after = await PagesApi.OkAsync(await ItemsApi.UnpublishAsync(scenario.Admin, scenario.Site, scenario.Posts, id, ItemsApi.Revision(after)));
        Assert.Equal(0, after.GetProperty("schedules").GetArrayLength());
    }

    [Fact]
    public async Task A_capture_made_invalid_by_a_schema_change_fails_at_its_time_and_the_item_needs_attention()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-fails");
        var item = await scenario.PostAsync("fragile");
        var id = ItemsApi.Id(item);
        await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour, ItemsApi.Revision(item)));

        // The summary field becomes required, with no fill-in: the captured content now lacks it.
        var posts = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts);
        var schema = CmsApi.Schema(posts);
        CmsApi.Fields(schema).Single(f => f!["key"]!.GetValue<string>() == "reading-time")!["required"] = true;
        await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.Site, posts, schema);

        await MakeDueAsync(scenario);
        await DispatchAsync(scenario);

        var after = await ItemsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts, id);
        Assert.Equal("draft", after.GetProperty("status").GetString());
        Assert.True(after.GetProperty("attention").GetBoolean());
        Assert.Contains(("schedule", "schedule_failed"), ItemsApi.Problems(after.GetProperty("report")));
        var failed = (await PagesApi.OkAsync(await ItemsApi.HistoryAsync(scenario.Admin, scenario.Site, scenario.Posts, id))).GetProperty("events")[0];
        Assert.Equal(("scheduleFailed", "cms.item_needs_attention"), (failed.GetProperty("kind").GetString(), failed.GetProperty("failureCode").GetString()));
    }

    [Fact]
    public async Task Schema_changes_migrate_captured_content_with_fill_ins()
    {
        using var scenario = await ItemsScenario.CreateAsync(database, "items-schedule-migrate");
        var item = await scenario.PostAsync("captured", new JsonObject { ["f1"] = "zz-canary-t", ["f3"] = 9 });
        var id = ItemsApi.Id(item);
        await PagesApi.OkAsync(await ItemsApi.ScheduleAsync(scenario.Admin, scenario.Site, scenario.Posts, id, "publish", InAnHour, ItemsApi.Revision(item)));

        var posts = await CmsApi.GetOkAsync(scenario.Admin, scenario.Site, scenario.Posts);
        var schema = CmsApi.Schema(posts);
        var fields = CmsApi.Fields(schema);
        fields.Remove(fields.Single(f => f!["key"]!.GetValue<string>() == "reading-time"));
        fields.Add(CmsApi.Field("~1", "subtitle", "text", settings: new { multiline = false, maxLength = 100 }));
        await CmsApi.ConfirmAndSaveAsync(scenario.Admin, scenario.Site, posts, schema, new Dictionary<string, object> { ["~1"] = "zz-canary-filled" });

        var captured = await scenario.Cms.WithDatabaseAsync(async cms =>
            (await cms.ForSite<ItemSchedule>(scenario.Site).SingleAsync(s => s.ItemId == id)).CapturedValues!.RootElement.GetRawText());
        Assert.DoesNotContain("\"f3\"", captured, StringComparison.Ordinal);
        Assert.Contains("zz-canary-filled", captured, StringComparison.Ordinal);
    }

    internal static Task<int> MakeDueAsync(ItemsScenario scenario) =>
        scenario.Cms.WithDatabaseAsync(cms => cms.Database.ExecuteSqlRawAsync(
            "UPDATE cms.outbox_messages SET available_at_utc = now() - interval '1 second' WHERE event_type = 'Cms.ItemScheduleDue' AND completed_at_utc IS NULL"));

    internal static async Task DispatchAsync(ItemsScenario scenario)
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(scenario.Cms.App.Services);
        var settings = scenario.Cms.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }
}
