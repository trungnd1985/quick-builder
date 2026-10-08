using System.Text.Json;
using QuickBuilder.Cms.Domain;

namespace QuickBuilder.UnitTests.Cms.Items;

/// <summary>An item's status follows what differs between its draft and published copies (015 FR-004, research IT-04).</summary>
public sealed class ItemStatusTests
{
    private static ItemValues Values(string json) =>
        ItemValues.Read(JsonDocument.Parse($$"""{ "schemaVersion": 1, "values": {{json}} }"""));

    [Fact]
    public void Nothing_published_is_a_draft() =>
        Assert.Equal(ItemStatuses.Draft, ItemStatuses.Compute(Values("""{ "f1": "a" }"""), "a", null, null));

    [Fact]
    public void Equal_values_and_slug_are_published_whatever_the_property_order() =>
        Assert.Equal(
            ItemStatuses.Published,
            ItemStatuses.Compute(Values("""{ "f1": "a", "f2": 2 }"""), "a", Values("""{ "f2": 2, "f1": "a" }"""), "a"));

    [Fact]
    public void A_different_value_is_changed() =>
        Assert.Equal(
            ItemStatuses.Changed,
            ItemStatuses.Compute(Values("""{ "f1": "b" }"""), "a", Values("""{ "f1": "a" }"""), "a"));

    [Fact]
    public void A_different_slug_is_changed() =>
        Assert.Equal(
            ItemStatuses.Changed,
            ItemStatuses.Compute(Values("""{ "f1": "a" }"""), "b", Values("""{ "f1": "a" }"""), "a"));

    [Fact]
    public void Undoing_an_edit_by_hand_returns_to_published()
    {
        var item = Item.New(Guid.NewGuid(), Guid.NewGuid(), "a", Values("""{ "f1": "a" }"""), Guid.NewGuid(), DateTimeOffset.UtcNow);
        item.Publish(item.DraftValues(), item.Slug, ItemVersionKinds.Publish, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        item.SaveDraft("a", Values("""{ "f1": "b" }"""), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(ItemStatuses.Changed, item.Status);

        item.SaveDraft("a", Values("""{ "f1": "a" }"""), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(ItemStatuses.Published, item.Status);
    }
}
