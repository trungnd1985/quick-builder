using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Outbox;
using Xunit;

namespace QuickBuilder.IntegrationTests.Publishing;

/// <summary>
/// User Story 5, SC-007: a schedule runs exactly once at its time, with the content reviewed
/// when it was set (012 FR-019, FR-020, research PR-14). The outbox is driven by hand, and a
/// message is made due by moving its <c>available_at_utc</c> rather than by waiting.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ScheduledPublishExactlyOnceTests(DatabaseFixture database) : IAsyncLifetime
{
    private PagesScenario _scenario = null!;
    private Guid _about;
    private string _aboutPath = string.Empty;

    public async Task InitializeAsync()
    {
        _scenario = await PagesScenario.CreateAsync(database, "schedule-once").ConfigureAwait(false);
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
    public async Task A_due_publish_runs_once_with_what_was_reviewed_and_says_who_set_it()
    {
        await SchedulePublishAsync(_aboutPath).ConfigureAwait(true);
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("Written later")).ConfigureAwait(true);

        await MakeDueAsync().ConfigureAwait(true);
        await DispatchAsync().ConfigureAwait(true);
        await DispatchAsync().ConfigureAwait(true);

        var html = await PageAsync().ConfigureAwait(true);
        Assert.Contains(">Reviewed<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Written later", html, StringComparison.Ordinal);
        Assert.Equal(1, await VersionCountAsync().ConfigureAwait(true));

        var schedule = await ScheduleAsync().ConfigureAwait(true);
        Assert.Equal(ScheduleStates.Done, schedule.State);

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        var entry = history.GetProperty("entries")[0];
        Assert.Equal("scheduled_publish", entry.GetProperty("action").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, entry.GetProperty("actor").ValueKind);
        Assert.Equal(schedule.CreatedBy, entry.GetProperty("scheduleOwner").GetProperty("id").GetGuid());

        // The draft moved on after the schedule was set, so it still reads as changed.
        Assert.True(history.GetProperty("publication").GetProperty("hasUnpublishedChanges").GetBoolean());
    }

    [Fact]
    public async Task Two_dispatchers_at_once_produce_one_version()
    {
        await SchedulePublishAsync(_aboutPath).ConfigureAwait(true);
        await MakeDueAsync().ConfigureAwait(true);

        await Task.WhenAll(DispatchAsync(), DispatchAsync(), DispatchAsync()).ConfigureAwait(true);

        Assert.Equal(1, await VersionCountAsync().ConfigureAwait(true));
        Assert.Equal(ScheduleStates.Done, (await ScheduleAsync().ConfigureAwait(true)).State);
    }

    [Fact]
    public async Task Running_the_same_schedule_again_or_an_old_revision_does_nothing()
    {
        await SchedulePublishAsync(_aboutPath).ConfigureAwait(true);
        await SchedulePublishAsync(_aboutPath, hours: 2).ConfigureAwait(true);
        var schedule = await ScheduleAsync().ConfigureAwait(true);
        Assert.Equal(2, schedule.Revision);

        // Revision 1's message is stale.
        await RunAsync(schedule.Id, revision: 1).ConfigureAwait(true);
        Assert.Equal(0, await VersionCountAsync().ConfigureAwait(true));
        Assert.Equal(ScheduleStates.Pending, (await ScheduleAsync().ConfigureAwait(true)).State);

        await RunAsync(schedule.Id, revision: 2).ConfigureAwait(true);
        await RunAsync(schedule.Id, revision: 2).ConfigureAwait(true);

        Assert.Equal(1, await VersionCountAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task A_publish_whose_component_was_taken_down_fails_and_changes_nothing()
    {
        var header = (await PagesApi.CreateComponentOkAsync(_scenario.Admin, _scenario.SiteA, "Header").ConfigureAwait(true)).GetProperty("id").GetGuid();
        var headerPath = PagesApi.ComponentPath(_scenario.SiteA, header);
        await PublishingApi.SaveAsync(_scenario.Admin, headerPath, PublishingApi.ComponentDocument("Header")).ConfigureAwait(true);
        await PublishingApi.SaveAsync(_scenario.Admin, _aboutPath, PublishingApi.HeadingDocument("With header", header)).ConfigureAwait(true);
        await SchedulePublishAsync(_aboutPath).ConfigureAwait(true);

        await MakeDueAsync().ConfigureAwait(true);
        await DispatchAsync().ConfigureAwait(true);

        var schedule = await ScheduleAsync().ConfigureAwait(true);
        Assert.Equal(ScheduleStates.Failed, schedule.State);
        Assert.Equal("pages.unpublished_components", schedule.FailureCode);
        Assert.Equal(0, await VersionCountAsync().ConfigureAwait(true));
        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync().ConfigureAwait(true)).StatusCode);

        var node = await PagesApi.NodeAsync(_scenario.Admin, _scenario.SiteA, "/about").ConfigureAwait(true);
        var failed = node.GetProperty("publication").GetProperty("failedSchedule");
        Assert.Equal("pages.unpublished_components", failed.GetProperty("failureCode").GetString());

        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        var entry = history.GetProperty("entries")[0];
        Assert.Equal("schedule_failed", entry.GetProperty("action").GetString());
        Assert.Equal("pages.unpublished_components", entry.GetProperty("reasonCode").GetString());

        // The outbox message itself succeeded: the refusal is the schedule's, not the delivery's.
        Assert.Equal(0, await _scenario.ReadAsync(db => db.Set<OutboxMessage>().CountAsync(m => m.FailedAtUtc != null)).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_message_that_fell_due_while_nothing_ran_is_handled_on_the_next_pass()
    {
        await SchedulePublishAsync(_aboutPath).ConfigureAwait(true);

        // Not due yet: a pass does nothing.
        await DispatchAsync().ConfigureAwait(true);
        Assert.Equal(0, await VersionCountAsync().ConfigureAwait(true));

        // Long overdue, as after a restart.
        await MakeDueAsync(TimeSpan.FromHours(6)).ConfigureAwait(true);
        await DispatchAsync().ConfigureAwait(true);

        Assert.Equal(1, await VersionCountAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task A_scheduled_unpublish_takes_the_page_down_and_is_logged()
    {
        await PublishingApi.PublishCurrentAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, _aboutPath, "unpublish", DateTimeOffset.UtcNow.AddHours(1)).ConfigureAwait(true)).ConfigureAwait(true);

        await MakeDueAsync().ConfigureAwait(true);
        await DispatchAsync().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await VisitAsync().ConfigureAwait(true)).StatusCode);
        var history = await PagesApi.OkAsync(await PublishingApi.HistoryAsync(_scenario.Admin, _aboutPath).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal("scheduled_unpublish", history.GetProperty("entries")[0].GetProperty("action").GetString());
        Assert.Equal(ScheduleStates.Done, (await ScheduleAsync().ConfigureAwait(true)).State);
    }

    private async Task SchedulePublishAsync(string path, int hours = 1)
    {
        var draftVersion = await PublishingApi.DraftVersionAsync(_scenario.Admin, path).ConfigureAwait(false);
        await PagesApi.OkAsync(await PublishingApi.ScheduleAsync(_scenario.Admin, path, "publish", DateTimeOffset.UtcNow.AddHours(hours), draftVersion).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private Task<int> MakeDueAsync(TimeSpan? overdue = null) =>
        _scenario.ReadAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE pages.outbox_messages SET available_at_utc = now() - {0} WHERE event_type = 'Pages.PublishScheduleDue' AND completed_at_utc IS NULL",
            overdue ?? TimeSpan.FromSeconds(1)));

    private async Task DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher>(_scenario.App.Services);
        var settings = _scenario.App.Services.GetRequiredService<IOutboxSettingsProvider>().Get() with { Enabled = true };
        while (await dispatcher.RunOnceAsync(settings, CancellationToken.None).ConfigureAwait(false) > 0)
        {
        }
    }

    private async Task RunAsync(Guid scheduleId, int revision)
    {
        await using var scope = _scenario.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ScheduleWrites>().RunAsync(_scenario.SiteA, scheduleId, revision, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>The page's schedule; each test sets one.</summary>
    private Task<PublishSchedule> ScheduleAsync() =>
        _scenario.ReadAsync(db => PagesScenario.Everything<PublishSchedule>(db).SingleAsync(s => s.SubjectId == _about));

    private Task<int> VersionCountAsync() =>
        _scenario.ReadAsync(db => PagesScenario.Everything<PublishedVersion>(db).CountAsync(v => v.SubjectId == _about));

    private Task<string> PageAsync() =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetStringAsync(new Uri("/about", UriKind.Relative));

    private Task<HttpResponseMessage> VisitAsync() =>
        _scenario.App.CreateClientForHost(PagesScenario.HostA).GetAsync(new Uri("/about", UriKind.Relative));
}
