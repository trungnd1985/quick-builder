namespace QuickBuilder.Pages.Contracts;

/// <summary>
/// Which pages and components use a collection, in their drafts, live versions or pending
/// scheduled content (016 research TB-13, module-surface.md §3).
/// </summary>
/// <remarks>
/// Pages keeps this in the transaction of every draft save, publish, rollback, schedule and
/// template change, so Cms can refuse to delete a collection that is in use (FR-010) and warn
/// before a schema change that removes or retypes a bound field (FR-011).
/// </remarks>
public interface ICollectionUsageReader
{
    /// <summary>Up to <paramref name="limit"/> pages and components that use the collection, and the total.</summary>
    Task<CollectionUses> ListUsesAsync(Guid siteId, Guid collectionId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// For each of <paramref name="fieldIds"/>, how many pages and components read it through a
    /// binding, filter or sort key; fields no one reads are present with zero.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountFieldUsesAsync(
        Guid siteId, Guid collectionId, IReadOnlyCollection<string> fieldIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// A page or component that uses a collection. <see cref="Title"/> is content: render it as
/// text, never log it.
/// </summary>
/// <param name="Kind"><c>page</c> or <c>component</c>.</param>
/// <param name="States">Where it is used: <c>draft</c>, <c>live</c> and <c>scheduled</c>, in that order.</param>
public sealed record CollectionUse(string Kind, Guid Id, string Title, IReadOnlyList<string> States);

/// <summary>Some of the uses of a collection, and how many there are in all.</summary>
public sealed record CollectionUses(IReadOnlyList<CollectionUse> Uses, int Total)
{
    public static CollectionUses None { get; } = new([], 0);
}
