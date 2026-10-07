using System.Text.Json;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// One item of a collection, as stored data: its draft values and, once published, its
/// published values, kept apart (data-model §1.2, FR-015, Principle VII).
/// </summary>
/// <remarks>
/// This feature only migrates items when a schema changes. Creating, editing and publishing
/// them is feature 015, which adds its own columns and must write through
/// <c>CollectionWriteGate</c> (data-model §3.3).
/// </remarks>
internal sealed class Item : ISiteOwned, IDisposable
{
    private Item(Guid siteId, Guid collectionId, JsonDocument draft, JsonDocument? published, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        SiteId = siteId;
        CollectionId = collectionId;
        Draft = draft;
        Published = published;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    // EF Core materialisation.
    private Item()
    {
        Draft = null!;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid CollectionId { get; private set; }

    /// <summary>The draft values document (contract §3).</summary>
    public JsonDocument Draft { get; private set; }

    /// <summary>The published values document; null until the item is published (#15).</summary>
    public JsonDocument? Published { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>An item with the given values. Used by tests now and by #15 later.</summary>
    public static Item Create(Guid siteId, Guid collectionId, ItemValues draft, ItemValues? published, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(clock);
        return new Item(siteId, collectionId, draft.ToDocument(), published?.ToDocument(), clock.GetUtcNow());
    }

    public ItemValues DraftValues() => ItemValues.Read(Draft);

    public ItemValues? PublishedValues() => Published is null ? null : ItemValues.Read(Published);

    /// <summary>Stores migrated values. Draft and published copies are rewritten together (clarification Q1).</summary>
    public void Migrate(ItemValues draft, ItemValues? published, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(clock);

        Draft = draft.ToDocument();
        if (Published is not null && published is not null)
        {
            Published = published.ToDocument();
        }

        UpdatedAtUtc = clock.GetUtcNow();
    }

    public void Dispose()
    {
        Draft.Dispose();
        Published?.Dispose();
    }
}
