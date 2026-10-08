using System.Text.Json.Nodes;

namespace QuickBuilder.Cms.Domain;

/// <summary>An item's publishing status (015 FR-004, research IT-04).</summary>
internal static class ItemStatuses
{
    /// <summary>Nothing is published.</summary>
    public const string Draft = "draft";

    /// <summary>The published values and slug equal the draft's.</summary>
    public const string Published = "published";

    /// <summary>Published, and the draft differs from what is published.</summary>
    public const string Changed = "changed";

    public static bool IsKnown(string status) => status is Draft or Published or Changed;

    /// <summary>
    /// The status of an item with these values. Equality is JSON value equality, so property
    /// order does not matter, and undoing an edit by hand returns the item to <see cref="Published"/>.
    /// </summary>
    public static string Compute(ItemValues draft, string draftSlug, ItemValues? published, string? publishedSlug)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (published is null)
        {
            return Draft;
        }

        return SameContent(draft, draftSlug, published, publishedSlug) ? Published : Changed;
    }

    /// <summary>Whether two copies of an item hold the same values and slug.</summary>
    public static bool SameContent(ItemValues a, string? slugA, ItemValues b, string? slugB)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return string.Equals(slugA, slugB, StringComparison.Ordinal)
            && JsonNode.DeepEquals(a.Write(), b.Write());
    }
}
