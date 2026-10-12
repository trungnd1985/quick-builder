using System.Text.Json;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Domain.Events;

namespace QuickBuilder.UnitTests.Cms.Items;

/// <summary>The item aggregate's operations and what they record (015 data-model §3.1).</summary>
public sealed class ItemTests
{
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Collection = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static ItemValues Values(string title) =>
        ItemValues.Read(JsonDocument.Parse($$"""{ "schemaVersion": 1, "values": { "f1": "{{title}}" } }"""));

    private static Item NewItem() => Item.New(Site, Collection, "hello", Values("Hello"), Ann, T0);

    [Fact]
    public void A_new_item_is_a_draft_at_revision_1_with_a_created_entry_and_event()
    {
        var item = NewItem();

        Assert.Equal(ItemStatuses.Draft, item.Status);
        Assert.Equal(1, item.Revision);
        Assert.Equal(1, item.NextVersionNumber);
        Assert.Null(item.Published);
        Assert.Equal(Ann, item.CreatedBy);
        Assert.Equal(ItemHistoryKinds.Created, Assert.Single(item.NewHistory).Kind);
        Assert.IsType<ItemCreatedEvent>(Assert.Single(item.DomainEvents));
    }

    [Fact]
    public void Publishing_and_unpublishing_move_the_render_stamp_and_keep_the_first_publish_time()
    {
        // 016 research TB-10, data-model §1.4: the item page's cache key moves with what it shows.
        var item = NewItem();
        var stamp = item.RenderStamp;

        item.SaveDraft("hello", Values("Draft only"), Ann, T0);
        Assert.Equal(stamp, item.RenderStamp);

        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0);
        item.Unpublish(null, Ann, T0.AddHours(1));
        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0.AddHours(2));

        Assert.Equal(stamp + 3, item.RenderStamp);
        Assert.Equal(T0, item.FirstPublishedAtUtc);
        Assert.Equal(T0.AddHours(2), item.PublishedAtUtc);
    }

    [Fact]
    public void A_bad_slug_is_a_programming_error() =>
        Assert.Throws<ArgumentException>(() => Item.New(Site, Collection, "Not A Slug", Values("x"), Ann, T0));

    [Fact]
    public void Saving_a_draft_moves_the_revision_and_never_touches_published_values()
    {
        var item = NewItem();
        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0);
        var published = item.Published!.RootElement.GetRawText();
        item.ClearDomainEvents();

        item.SaveDraft("hello", Values("Changed"), Bob, T0.AddMinutes(1));

        Assert.Equal(3, item.Revision);
        Assert.Equal(published, item.Published!.RootElement.GetRawText());
        Assert.Equal(ItemStatuses.Changed, item.Status);
        Assert.Equal(Bob, item.UpdatedBy);
        Assert.IsType<ItemDraftSavedEvent>(Assert.Single(item.DomainEvents));
    }

    [Fact]
    public void Publishing_creates_a_numbered_version_and_makes_it_live()
    {
        var item = NewItem();
        item.ClearNewHistory();

        var first = item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0);
        var second = item.Publish(item.DraftValues(), "hello-again", ItemVersionKinds.Publish, null, null, Bob, T0.AddHours(1));

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.Equal(2, item.LiveVersion);
        Assert.Equal(3, item.NextVersionNumber);
        Assert.Equal("hello-again", item.PublishedSlug);
        Assert.Equal(Bob, item.PublishedBy);
        Assert.All(item.NewHistory, e => Assert.Equal(ItemHistoryKinds.Published, e.Kind));

        var events = item.DomainEvents.OfType<ItemPublishedEvent>().ToList();
        Assert.False(events[0].SlugChanged);
        Assert.True(events[1].SlugChanged);
    }

    [Fact]
    public void A_rollback_is_recorded_as_one_with_what_it_restored()
    {
        var item = NewItem();
        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0);
        item.ClearNewHistory();

        var version = item.Publish(Values("Old"), "hello", ItemVersionKinds.Rollback, restoredFrom: 1, null, Ann, T0);

        Assert.Equal(1, version.RestoredFrom);
        Assert.Equal(ItemHistoryKinds.RolledBack, Assert.Single(item.NewHistory).Kind);
        Assert.Equal("Hello", item.DraftValues().Get("f1")!.GetValue<string>());
    }

    [Fact]
    public void Unpublishing_keeps_the_draft_and_marks_scheduled_runs()
    {
        var item = NewItem();
        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0);
        item.ClearDomainEvents();
        item.ClearNewHistory();

        item.Unpublish(ItemScheduleActions.Unpublish, Ann, T0);

        Assert.Null(item.Published);
        Assert.Null(item.PublishedSlug);
        Assert.Null(item.LiveVersion);
        Assert.Equal(ItemStatuses.Draft, item.Status);
        Assert.Equal("Hello", item.DraftValues().Get("f1")!.GetValue<string>());
        Assert.Equal(ItemScheduleActions.Unpublish, Assert.Single(item.NewHistory).ScheduleAction);
        Assert.True(Assert.IsType<ItemUnpublishedEvent>(Assert.Single(item.DomainEvents)).Scheduled);
    }

    [Fact]
    public void Unpublishing_a_draft_is_a_programming_error() =>
        Assert.Throws<InvalidOperationException>(() => NewItem().Unpublish(null, Ann, T0));

    [Fact]
    public void Copying_a_version_into_the_draft_publishes_nothing()
    {
        var item = NewItem();
        var version = item.Publish(Values("Published"), "published", ItemVersionKinds.Publish, null, null, Ann, T0);
        item.SaveDraft("hello", Values("Draft"), Ann, T0);
        item.ClearDomainEvents();

        item.CopyToDraft(version, Bob, T0);

        Assert.Equal("Published", item.DraftValues().Get("f1")!.GetValue<string>());
        Assert.Equal("published", item.Slug);
        Assert.Equal(ItemStatuses.Published, item.Status);
        Assert.IsType<ItemDraftSavedEvent>(Assert.Single(item.DomainEvents));
    }

    [Fact]
    public void A_duplicate_is_a_new_draft_of_the_same_collection()
    {
        var item = NewItem();
        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0);

        var copy = item.Duplicate("hello-copy", Values("Hello (copy)"), Bob, T0);

        Assert.NotEqual(item.Id, copy.Id);
        Assert.Equal(item.CollectionId, copy.CollectionId);
        Assert.Equal(ItemStatuses.Draft, copy.Status);
        Assert.Null(copy.Published);
        Assert.Equal(1, copy.NextVersionNumber);
    }

    [Fact]
    public void Deleting_reports_whether_it_was_published()
    {
        var item = NewItem();
        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Ann, T0);
        item.ClearDomainEvents();

        item.MarkDeleted(T0);

        Assert.True(Assert.IsType<ItemDeletedEvent>(Assert.Single(item.DomainEvents)).WasPublished);
    }

    [Fact]
    public void Stored_data_from_before_items_had_slugs_gets_a_placeholder()
    {
        var item = Item.Create(Site, Collection, Values("x"), Values("x"), TimeProvider.System);

        Assert.Equal(Item.PlaceholderSlug(item.Id), item.Slug);
        Assert.Equal(item.Slug, item.PublishedSlug);
        Assert.True(SlugRules.IsValid(item.Slug));

        item.ReplacePlaceholderSlug("from-name");
        Assert.Equal("from-name", item.PublishedSlug);
    }

    [Fact]
    public void A_schema_migration_moves_the_revision()
    {
        var item = NewItem();

        item.Migrate(Values("Converted"), null, TimeProvider.System);

        Assert.Equal(2, item.Revision);
    }
}
