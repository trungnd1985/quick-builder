using System.Text.Json;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>
/// An item's draft revision moves only when its draft does (023 research CE-08, data-model
/// §3.1), and remembers who changed the draft, when, and whether it was a member or a schema
/// change (CE-09).
/// </summary>
public sealed class ItemDraftRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();

    private static ItemValues Values(string json) =>
        ItemValues.Read(JsonDocument.Parse($$"""{ "schemaVersion": 1, "values": {{json}} }"""));

    private static Item NewItem() => Item.New(Guid.NewGuid(), Guid.NewGuid(), "post", Values("""{ "f1": "a" }"""), Anna, Now);

    [Fact]
    public void A_new_item_starts_at_draft_revision_1_changed_by_its_creator()
    {
        var item = NewItem();

        Assert.Equal(1, item.DraftRevision);
        Assert.Equal(Anna, item.DraftChangedBy);
        Assert.Equal(Now, item.DraftChangedAtUtc);
        Assert.Equal(ItemDraftChangeKinds.Member, item.DraftChangeKind);
    }

    [Fact]
    public void Saving_the_draft_moves_the_draft_revision_and_records_the_member()
    {
        var item = NewItem();

        item.SaveDraft("post", Values("""{ "f1": "b" }"""), Ben, Now.AddMinutes(1));

        Assert.Equal(2, item.DraftRevision);
        Assert.Equal(Ben, item.DraftChangedBy);
        Assert.Equal(Now.AddMinutes(1), item.DraftChangedAtUtc);
        Assert.Equal(ItemDraftChangeKinds.Member, item.DraftChangeKind);
    }

    [Fact]
    public void Publishing_unpublishing_and_schedules_move_the_revision_but_not_the_draft_revision()
    {
        var item = NewItem();
        var revision = item.Revision;

        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ben, Now.AddMinutes(1));
        item.Unpublish(null, Ben, Now.AddMinutes(2));
        item.RecordSchedule(ItemHistoryKinds.Scheduled, "publish", Now.AddDays(1), null, Ben, Now.AddMinutes(3));

        Assert.True(item.Revision > revision);
        Assert.Equal(1, item.DraftRevision);
        Assert.Equal(Anna, item.DraftChangedBy);
        Assert.Equal(Now, item.DraftChangedAtUtc);
    }

    [Fact]
    public void Copying_a_version_into_the_draft_moves_the_draft_revision()
    {
        var item = NewItem();
        var version = item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Anna, Now);
        item.SaveDraft("post", Values("""{ "f1": "b" }"""), Anna, Now.AddMinutes(1));

        item.CopyToDraft(version, Ben, Now.AddMinutes(2));

        Assert.Equal(3, item.DraftRevision);
        Assert.Equal(Ben, item.DraftChangedBy);
        Assert.Equal(ItemDraftChangeKinds.Member, item.DraftChangeKind);
    }

    [Fact]
    public void A_schema_change_that_rewrites_the_draft_is_a_system_change()
    {
        var item = NewItem();
        var clock = new FixedClock(Now.AddHours(1));

        item.Migrate(Values("""{ "f1": "a", "f2": "filled" }"""), null, clock);

        Assert.Equal(2, item.DraftRevision);
        Assert.Null(item.DraftChangedBy);
        Assert.Equal(Now.AddHours(1), item.DraftChangedAtUtc);
        Assert.Equal(ItemDraftChangeKinds.SchemaChange, item.DraftChangeKind);
    }

    [Fact]
    public void A_schema_change_that_leaves_the_draft_as_it_was_does_not_move_the_draft_revision()
    {
        var item = NewItem();
        var revision = item.Revision;

        item.Migrate(Values("""{ "f1": "a" }"""), null, new FixedClock(Now.AddHours(1)));

        Assert.True(item.Revision > revision);
        Assert.Equal(1, item.DraftRevision);
        Assert.Equal(Anna, item.DraftChangedBy);
        Assert.Equal(ItemDraftChangeKinds.Member, item.DraftChangeKind);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
