using QuickBuilder.Editor.Domain;

namespace QuickBuilder.UnitTests.Editor;

/// <summary>
/// Expiry, staleness, claims, takeovers and moves of editing sessions (023 data-model §1.1,
/// research CE-03 to CE-06).
/// </summary>
public sealed class EditingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();

    private static EditingRules.Holder HolderOf(Guid user, TimeSpan seenAgo, TimeSpan activeAgo, Guid? session = null) =>
        new(session ?? Guid.NewGuid(), user, Now - seenAgo, Now - activeAgo);

    [Fact]
    public void A_session_is_gone_exactly_two_minutes_after_it_was_last_seen()
    {
        Assert.False(EditingRules.IsGone(Now - TimeSpan.FromSeconds(119), Now));
        Assert.True(EditingRules.IsGone(Now - TimeSpan.FromMinutes(2), Now));
    }

    [Fact]
    public void A_lock_is_stale_exactly_thirty_minutes_after_the_last_activity()
    {
        Assert.False(EditingRules.IsStale(Now - TimeSpan.FromSeconds((30 * 60) - 1), Now));
        Assert.True(EditingRules.IsStale(Now - TimeSpan.FromMinutes(30), Now));
    }

    [Fact]
    public void A_claim_is_allowed_with_no_holder_or_a_gone_one_and_refused_otherwise()
    {
        Assert.True(EditingRules.MayClaim(null, Now));
        Assert.True(EditingRules.MayClaim(HolderOf(Anna, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)), Now));
        Assert.False(EditingRules.MayClaim(HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)), Now));

        // A stale holder still holds the lock: only a takeover moves it.
        Assert.False(EditingRules.MayClaim(HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1)), Now));
    }

    [Fact]
    public void A_takeover_needs_the_named_holder_to_be_another_member_and_stale()
    {
        var session = Guid.NewGuid();
        var stale = HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(31), session);
        var active = HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(29), session);

        Assert.True(EditingRules.CanTakeOver(stale, session, Ben, Now));
        Assert.False(EditingRules.CanTakeOver(active, session, Ben, Now));
        Assert.False(EditingRules.CanTakeOver(stale, Guid.NewGuid(), Ben, Now));
        Assert.False(EditingRules.CanTakeOver(stale, session, Anna, Now));
        Assert.False(EditingRules.CanTakeOver(null, session, Ben, Now));
    }

    [Fact]
    public void A_gone_holder_is_not_taken_over_but_simply_claimed()
    {
        var session = Guid.NewGuid();
        var gone = HolderOf(Anna, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(40), session);

        Assert.False(EditingRules.CanTakeOver(gone, session, Ben, Now));
        Assert.True(EditingRules.MayClaim(gone, Now));
    }

    [Fact]
    public void A_move_needs_the_named_holder_to_be_the_same_member_whether_active_or_stale()
    {
        var session = Guid.NewGuid();

        Assert.True(EditingRules.CanMove(HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), session), session, Anna, Now));
        Assert.True(EditingRules.CanMove(HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromHours(2), session), session, Anna, Now));
        Assert.False(EditingRules.CanMove(HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), session), session, Ben, Now));
        Assert.False(EditingRules.CanMove(HolderOf(Anna, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), session), Guid.NewGuid(), Anna, Now));
    }

    [Fact]
    public void A_viewer_with_no_activity_is_never_stale_and_activity_is_ignored_for_viewers()
    {
        var viewer = EditingSession.Open(SiteId, "page", Guid.NewGuid(), null, Anna, Now);

        viewer.Seen(active: true, Now + TimeSpan.FromMinutes(1));

        Assert.False(viewer.HoldsLock);
        Assert.Null(viewer.LastActivityAt);
        Assert.False(viewer.IsStale(Now + TimeSpan.FromHours(5)));
        Assert.Equal(Now + TimeSpan.FromMinutes(1), viewer.LastSeenAt);
    }

    [Fact]
    public void Taking_the_lock_starts_the_activity_clock_and_activity_moves_it()
    {
        var session = EditingSession.Open(SiteId, "page", Guid.NewGuid(), null, Anna, Now);

        session.TakeLock(Now + TimeSpan.FromSeconds(10));
        Assert.True(session.HoldsLock);
        Assert.Equal(Now + TimeSpan.FromSeconds(10), session.LockTakenAt);
        Assert.Equal(Now + TimeSpan.FromSeconds(10), session.LastActivityAt);

        session.Seen(active: false, Now + TimeSpan.FromMinutes(20));
        Assert.Equal(Now + TimeSpan.FromSeconds(10), session.LastActivityAt);

        session.Seen(active: true, Now + TimeSpan.FromMinutes(25));
        Assert.Equal(Now + TimeSpan.FromMinutes(25), session.LastActivityAt);
        Assert.False(session.IsStale(Now + TimeSpan.FromMinutes(54)));
        Assert.True(session.IsStale(Now + TimeSpan.FromMinutes(55)));
    }

    [Fact]
    public void Losing_the_lock_records_who_took_it_and_why()
    {
        var session = EditingSession.Open(SiteId, "item", Guid.NewGuid(), Guid.NewGuid(), Anna, Now);
        session.TakeLock(Now);

        session.LoseLock(Ben, EditingSession.LostReasons.Takeover, Now + TimeSpan.FromHours(1));

        Assert.False(session.HoldsLock);
        Assert.Null(session.LockTakenAt);
        Assert.Null(session.LastActivityAt);
        Assert.Equal(Ben, session.LostLockToUserId);
        Assert.Equal("takeover", session.LostLockReason);
        Assert.Equal(Now + TimeSpan.FromHours(1), session.LostLockAt);

        // Claiming again clears the record of the loss.
        session.TakeLock(Now + TimeSpan.FromHours(2));
        Assert.Null(session.LostLockReason);
        Assert.Null(session.LostLockToUserId);
        Assert.Null(session.LostLockAt);
    }

    [Fact]
    public void Opening_refuses_an_unknown_kind_and_items_need_their_collection()
    {
        Assert.Throws<ArgumentException>(() => EditingSession.Open(SiteId, "menu", Guid.NewGuid(), null, Anna, Now));
        Assert.Throws<ArgumentException>(() => EditingSession.Open(SiteId, "item", Guid.NewGuid(), null, Anna, Now));
    }
}
