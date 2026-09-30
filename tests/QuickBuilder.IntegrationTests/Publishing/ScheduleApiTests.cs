using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 5: setting, changing and cancelling schedules, and manual actions superseding
/// them (012 FR-018, FR-019, FR-021, research PR-14).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ScheduleApiTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _about;
    private string _aboutPath = string.Empty;

    private static DateTimeOffset InAnHour => DateTimeOffset.UtcNow.AddHours(1);

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "schedule").ConfigureAwait(false);
        _about = (await PagesApi.SeedTreeAsync(_scenario.Admin, _scenario.SiteA, "about").ConfigureAwait(false))["/about"];
        _aboutPath = PagesApi.PagePath(_scenario.SiteA, _about);
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("Reviewed")).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _scenario.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Scheduling_a_publish_captures_the_reviewed_draft_and_writes_a_delayed_message()
    {
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        var due = InAnHour;

        var schedule = await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", due, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal("publish", schedule.GetProperty("action").GetString());
        Assert.Equal("pending", schedule.GetProperty("state").GetString());
        Assert.Equal(draftVersion, schedule.GetProperty("capturedDraftVersion").GetUInt32());
        Assert.Equal(due.ToUnixTimeSeconds(), schedule.GetProperty("dueAtUtc").GetDateTimeOffset().ToUnixTimeSeconds());

        var stored = await _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).SingleAsync(s => s.SubjectId == _about)).ConfigureAwait(true);
        Assert.Contains("Reviewed", stored.CapturedDocument, StringComparison.Ordinal);
        Assert.Equal(1, stored.Revision);

        var outbox = await _scenario.OutboxAsync().ConfigureAwait(true);
        Assert.Single(outbox, m => m.Type == "Pages.PublishScheduleDue");

        var node = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/about").ConfigureAwait(true);
        var pending = Assert.Single(node.GetProperty("publication").GetProperty("pendingSchedules").EnumerateArray());
        Assert.Equal("publish", pending.GetProperty("action").GetString());

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("schedule_set", history.GetProperty("entries")[0].GetProperty("action").GetString());
    }

    [Fact]
    public async Task A_second_schedule_moves_the_time_and_the_revision()
    {
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", InAnHour, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        var later = DateTimeOffset.UtcNow.AddHours(3);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", later, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        var schedules = await _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).Where(s => s.SubjectId == _about).ToListAsync()).ConfigureAwait(true);
        var only = Assert.Single(schedules);
        Assert.Equal(2, only.Revision);
        Assert.Equal(later.ToUnixTimeSeconds(), only.DueAtUtc.ToUnixTimeSeconds());
        Assert.Equal(2, (await _scenario.OutboxAsync().ConfigureAwait(true)).Count(m => m.Type == "Pages.PublishScheduleDue"));

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("schedule_changed", history.GetProperty("entries")[0].GetProperty("action").GetString());
    }

    [Fact]
    public async Task Bad_schedules_are_refused()
    {
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", DateTimeOffset.UtcNow.AddMinutes(-1), draftVersion).ConfigureAwait(true),
            HttpStatusCode.BadRequest,
            "pages.schedule_in_past").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", InAnHour, draftVersion + 1).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.out_of_date").ConfigureAwait(true);
        await PagesApi.RefusedAsync(
            await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", InAnHour).ConfigureAwait(true),
            HttpStatusCode.BadRequest,
            "pages.draft_version_required").ConfigureAwait(true);

        // Nothing to take down yet.
        await PagesApi.RefusedAsync(
            await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "unpublish", InAnHour).ConfigureAwait(true),
            HttpStatusCode.Conflict,
            "pages.not_published").ConfigureAwait(true);

        Assert.Equal(0, await _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).CountAsync(s => s.SubjectId == _about)).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_draft_that_does_not_pass_the_schema_cannot_be_scheduled()
    {
        // The contract moved on after the draft was saved: stored directly, as an old draft would be.
        await _scenario.ReadAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE pages.page_drafts SET document = CAST({0} AS jsonb) WHERE page_id = {1}",
            """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[{"id":"x","type":"no-such-block","props":{},"children":[]}]}}""",
            _about)).ConfigureAwait(true);
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        await PagesApi.RefusedAsync(
            await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", InAnHour, draftVersion).ConfigureAwait(true),
            HttpStatusCode.BadRequest,
            "pages.invalid_document").ConfigureAwait(true);
    }

    [Fact]
    public async Task Cancelling_ends_the_schedule_and_a_second_cancel_finds_nothing()
    {
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", InAnHour, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await PublishingApi.CancelScheduleAsync(_scenario.Admin, _aboutPath, "publish").ConfigureAwait(true)).StatusCode);
        await PagesApi.RefusedAsync(
            await PublishingApi.CancelScheduleAsync(_scenario.Admin, _aboutPath, "publish").ConfigureAwait(true),
            HttpStatusCode.NotFound,
            "pages.schedule_not_found").ConfigureAwait(true);

        var stored = await _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).SingleAsync(s => s.SubjectId == _about)).ConfigureAwait(true);
        Assert.Equal(ScheduleStates.Cancelled, stored.State);
        var node = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/about").ConfigureAwait(true);
        Assert.Equal(0, node.GetProperty("publication").GetProperty("pendingSchedules").GetArrayLength());
    }

    [Fact]
    public async Task A_manual_publish_supersedes_the_pending_scheduled_publish()
    {
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", InAnHour, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);

        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);

        var stored = await _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).SingleAsync(s => s.SubjectId == _about)).ConfigureAwait(true);
        Assert.Equal(ScheduleStates.Cancelled, stored.State);
        var entries = await EntriesAsync().ConfigureAwait(true);
        var superseded = Assert.Single(entries, e => e.GetProperty("action").GetString() == "schedule_cancelled");
        Assert.Equal("superseded_by_manual_action", superseded.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task A_manual_unpublish_supersedes_both_pending_schedules_and_a_rollback_the_publish_one()
    {
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "publish", InAnHour, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "unpublish", InAnHour.AddHours(1)).ConfigureAwait(true)).ConfigureAwait(true);

        await PagesApi.OkAsync(await PublishingApi.RollbackAsync(_scenario.Admin, _aboutPath, 1).ConfigureAwait(true)).ConfigureAwait(true);
        var afterRollback = await StatesAsync().ConfigureAwait(true);
        Assert.Equal(ScheduleStates.Cancelled, afterRollback[ScheduleActions.Publish]);
        Assert.Equal(ScheduleStates.Pending, afterRollback[ScheduleActions.Unpublish]);

        await PagesApi.OkAsync(await PublishingApi.UnpublishAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(ScheduleStates.Cancelled, (await StatesAsync().ConfigureAwait(true))[ScheduleActions.Unpublish]);
    }

    [Fact]
    public async Task A_component_schedules_its_publish_too()
    {
        var header = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var path = PagesApi.ComponentPath(_scenario.SiteA, header);
        await PublishingApi.SaveAsync(_scenario.Admin, path, PublishingApi.ComponentDocument("Header")).ConfigureAwait(true);
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, path).ConfigureAwait(true);

        var schedule = await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, path, "publish", InAnHour, draftVersion).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("pending", schedule.GetProperty("state").GetString());

        var component = await PagesApi.OkAsync(await PagesApi.GetComponentAsync(_scenario.Admin, _scenario.SiteA, header).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Single(component.GetProperty("publication").GetProperty("pendingSchedules").EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await PublishingApi.CancelScheduleAsync(_scenario.Admin, path, "publish").ConfigureAwait(true)).StatusCode);

        // Components have no unpublish schedule.
        var unpublish = await PublishingApi.ScheduleAsync(_scenario.Admin, path, "unpublish", InAnHour).ConfigureAwait(true);
        Assert.False(unpublish.IsSuccessStatusCode);
        Assert.Equal(0, await _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db)
            .CountAsync(s => s.SubjectId == header && s.Action == ScheduleActions.Unpublish)).ConfigureAwait(true));
    }

    private async Task<Dictionary<string, string>> StatesAsync() =>
        (await _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).Where(s => s.SubjectId == _about).ToListAsync()).ConfigureAwait(false))
        .ToDictionary(s => s.Action, s => s.State);

    private async Task<List<JsonElement>> EntriesAsync()
    {
        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(false)).ConfigureAwait(false);
        return [.. history.GetProperty("entries").EnumerateArray()];
    }
}
