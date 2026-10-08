using System.Text.Json;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Cms.Domain;

/// <summary>How a version came about (015 data-model §1.6).</summary>
internal static class ItemVersionKinds
{
    public const string Publish = "publish";
    public const string Scheduled = "scheduled";
    public const string Rollback = "rollback";
}

/// <summary>
/// One published version of an item: a full copy of its published values and slug (015 FR-019,
/// research IT-08), as 012 keeps for pages.
/// </summary>
/// <remarks>
/// Never changed by a member once stored. A schema change migrates its values like every other
/// copy of the item, so removed values leave it too (research IT-12, 014 FR-024); fill-in
/// values are never written to it, because it records what was published.
/// </remarks>
internal sealed class ItemVersion : ISiteOwned, IDisposable
{
    internal ItemVersion(
        Guid siteId,
        Guid itemId,
        int number,
        ItemValues values,
        string slug,
        string kind,
        int? restoredFrom,
        Guid publishedBy,
        DateTimeOffset publishedAtUtc)
    {
        SiteId = siteId;
        ItemId = itemId;
        Number = number;
        Values = values.ToDocument();
        Slug = slug;
        Kind = kind;
        RestoredFrom = restoredFrom;
        PublishedBy = publishedBy;
        PublishedAtUtc = publishedAtUtc;
    }

    // EF Core materialisation.
    private ItemVersion()
    {
        Values = null!;
        Slug = string.Empty;
        Kind = string.Empty;
    }

    public Guid ItemId { get; private set; }

    /// <summary>1, 2, 3 … per item, never reused.</summary>
    public int Number { get; private set; }

    public Guid SiteId { get; private set; }

    public JsonDocument Values { get; private set; }

    public string Slug { get; private set; }

    /// <summary>One of <see cref="ItemVersionKinds"/>.</summary>
    public string Kind { get; private set; }

    public int? RestoredFrom { get; private set; }

    /// <summary>Who published it; for a scheduled publish, who set the schedule.</summary>
    public Guid PublishedBy { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }

    public ItemValues ReadValues() => ItemValues.Read(Values);

    /// <summary>Stores values converted by a schema change (research IT-12).</summary>
    public void Migrate(ItemValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values.ToDocument();
    }

    public void Dispose() => Values?.Dispose();
}
