using System.Text.Json;

namespace QuickBuilder.Cms.Contracts.Publishing;

/// <summary>
/// Published collection items, as the public site reads them (016 research TB-08,
/// module-surface.md §2).
/// </summary>
/// <remarks>
/// Only items with published data are visible here, and only their published data: never a
/// draft (constitution Principle VII). The Cms module implements it over a read-only
/// connection, as Pages does for published pages (012 research PR-06), so the public path
/// cannot write content even by mistake. Another site's ids are simply absent.
/// </remarks>
public interface IPublishedItemReader
{
    /// <summary>
    /// The published item of <paramref name="collectionId"/> whose published slug is
    /// <paramref name="slug"/> (lower case), with its render stamp; null when there is none.
    /// One indexed lookup.
    /// </summary>
    Task<PublishedItemRef?> FindBySlugAsync(Guid siteId, Guid collectionId, string slug, CancellationToken cancellationToken = default);

    /// <summary>The published items among <paramref name="itemIds"/>; ids without published data are absent.</summary>
    Task<IReadOnlyDictionary<Guid, PublishedItem>> GetAsync(
        Guid siteId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>One page of a list's items and how many items match in all (binding-expression.md §5).</summary>
    Task<PublishedItemPage> QueryAsync(Guid siteId, PublishedListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current schema documents of <paramref name="collectionIds"/> (collection-schema.md §2),
    /// so the renderer knows field types, option labels and settings. Absent ids are not
    /// collections of the site.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, JsonElement>> GetSchemasAsync(
        Guid siteId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken = default);

    /// <summary>How many items of the collection have published data, for address warnings (TB-12).</summary>
    Task<int> CountPublishedAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken = default);
}

/// <summary>A published item found by slug, with the stamp its page's cache key carries (TB-10).</summary>
public sealed record PublishedItemRef(Guid ItemId, long RenderStamp);

/// <summary>
/// One item's published data. <see cref="Slug"/>, <see cref="Name"/> and <see cref="Values"/>
/// are content: render them encoded, never log them.
/// </summary>
/// <param name="Values">The published item values document (collection-schema.md §3).</param>
public sealed record PublishedItem(
    Guid Id,
    Guid CollectionId,
    string Slug,
    string? Name,
    DateTimeOffset FirstPublishedAtUtc,
    DateTimeOffset LastPublishedAtUtc,
    JsonElement Values);

/// <summary>One page of a list, and the number of items that match in all.</summary>
public sealed record PublishedItemPage(IReadOnlyList<PublishedItem> Items, int Total)
{
    public static PublishedItemPage Empty { get; } = new([], 0);
}
