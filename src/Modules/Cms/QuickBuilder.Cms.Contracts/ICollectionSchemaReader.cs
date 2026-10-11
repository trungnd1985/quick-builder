using System.Text.Json;

namespace QuickBuilder.Cms.Contracts;

/// <summary>
/// Collections and their schemas, for other modules' admin-side checks (016 research TB-06,
/// TB-08, TB-10; module-surface.md §2).
/// </summary>
/// <remarks>
/// Reads the current schema, on the normal connection. The public path reads schemas through
/// <see cref="Publishing.IPublishedItemReader"/> instead.
/// </remarks>
public interface ICollectionSchemaReader
{
    /// <summary>Which of <paramref name="collectionIds"/> are collections of the site.</summary>
    Task<IReadOnlySet<Guid>> ExistingAsync(Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default);

    /// <summary>The current schema documents of the site's collections among <paramref name="collectionIds"/>.</summary>
    Task<IReadOnlyDictionary<Guid, JsonElement>> GetSchemasAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The collections of the site that have a reference or multi-reference field targeting
    /// <paramref name="collectionId"/>, itself included when it refers to itself: the
    /// collections whose items can show its items (TB-10).
    /// </summary>
    Task<IReadOnlyList<Guid>> CollectionsReferencingAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many items of the collection have published data, for the admin's address warnings
    /// (TB-12). The public path's reader cannot answer on the admin surface, which resolves no site.
    /// </summary>
    Task<int> CountPublishedItemsAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default);
}
