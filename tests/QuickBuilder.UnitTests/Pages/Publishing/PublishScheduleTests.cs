using QuickBuilder.Pages.Domain;

namespace QuickBuilder.UnitTests.Pages.Publishing;

/// <summary>The schedule's states and transitions (012 data-model §4.2, research PR-14).</summary>
public sealed class PublishScheduleTests
{
    private const string Document = """{"schemaVersion":1,"root":{"id":"root","type":"root","props":{},"children":[]}}""";

    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private static PublishSchedule NewPublish() =>
        PublishSchedule.ForPublish(Site, SubjectKinds.Page, Guid.NewGuid(), Now.AddHours(1), Document, 1, "About", 7, Member, Now);

    [Fact]
    public void A_scheduled_publish_starts_pending_with_the_captured_draft()
    {
        var schedule = NewPublish();

        Assert.True(schedule.IsPending);
        Assert.Equal(ScheduleActions.Publish, schedule.Action);
        Assert.Equal(1, schedule.Revision);
        Assert.Equal(Document, schedule.CapturedDocument);
        Assert.Equal("About", schedule.CapturedTitle);
        Assert.Equal(7, schedule.CapturedDraftVersion);
        Assert.Equal(Member, schedule.CreatedBy);
    }

    [Fact]
    public void A_scheduled_unpublish_captures_nothing()
    {
        var schedule = PublishSchedule.ForUnpublish(Site, Guid.NewGuid(), Now.AddHours(1), Member, Now);

        Assert.Equal(ScheduleActions.Unpublish, schedule.Action);
        Assert.Null(schedule.CapturedDocument);
        Assert.Equal(SubjectKinds.Page, schedule.SubjectKind);
    }

    [Fact]
    public void Changing_the_time_moves_the_revision_on_so_the_old_message_is_not_due()
    {
        var schedule = NewPublish();
        var other = Guid.NewGuid();

        schedule.Reschedule(Now.AddHours(2), other);

        Assert.Equal(2, schedule.Revision);
        Assert.Equal(Now.AddHours(2), schedule.DueAtUtc);
        Assert.Equal(other, schedule.CreatedBy);
        Assert.False(schedule.IsDue(1));
        Assert.True(schedule.IsDue(2));
    }

    [Fact]
    public void Recapturing_replaces_what_will_be_published()
    {
        var schedule = NewPublish();

        schedule.Recapture("""{"schemaVersion":1}""", 1, "About us", 9);

        Assert.Equal(9, schedule.CapturedDraftVersion);
        Assert.Equal("About us", schedule.CapturedTitle);
        Assert.Equal(1, schedule.Revision);
    }

    [Theory]
    [InlineData(ScheduleStates.Done)]
    [InlineData(ScheduleStates.Cancelled)]
    [InlineData(ScheduleStates.Failed)]
    public void A_finished_schedule_is_never_due_and_cannot_change(string state)
    {
        var schedule = NewPublish();
        switch (state)
        {
            case ScheduleStates.Done: schedule.Complete(Now); break;
            case ScheduleStates.Cancelled: schedule.Cancel(Now); break;
            default: schedule.Fail("pages.unpublished_components", Now); break;
        }

        Assert.Equal(state, schedule.State);
        Assert.Equal(Now, schedule.CompletedAtUtc);
        Assert.False(schedule.IsDue(schedule.Revision));
        Assert.Throws<InvalidOperationException>(() => schedule.Reschedule(Now.AddHours(3), Member));
        Assert.Throws<InvalidOperationException>(() => schedule.Complete(Now));
    }

    [Fact]
    public void A_failure_keeps_its_code()
    {
        var schedule = NewPublish();

        schedule.Fail("pages.unpublished_components", Now);

        Assert.Equal("pages.unpublished_components", schedule.FailureCode);
    }
}
