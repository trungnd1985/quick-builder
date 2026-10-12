using QuickBuilder.Cms.Contracts.Publishing;

namespace QuickBuilder.Rendering.Binding;

/// <summary>
/// The published items, schemas and list results one render reads, fetched before rendering
/// starts (016 research TB-09), so block renderers stay synchronous and do no I/O.
/// </summary>
/// <remarks>
/// Everything here is published data, read through <see cref="IPublishedItemReader"/> on a
/// read-only connection (Principle VII). A page with no bindings and no lists renders with
/// <see cref="Empty"/>.
/// </remarks>
internal sealed class ResolvedData
{
    /// <summary>Nothing fetched: a page with no bindings or lists, or a page that is not a template.</summary>
    public static ResolvedData Empty { get; } = new();

    /// <summary>The item whose address was requested, on a template page; null elsewhere.</summary>
    public PublishedItem? PageItem { get; init; }

    /// <summary>Current schemas by collection id, for every collection a binding or list reads.</summary>
    public IReadOnlyDictionary<Guid, CollectionSchemaInfo> Schemas { get; init; } = new Dictionary<Guid, CollectionSchemaInfo>();

    /// <summary>Items read through a reference (<c>via</c>) or a multi-reference source, by id; published only.</summary>
    public IReadOnlyDictionary<Guid, PublishedItem> Items { get; init; } = new Dictionary<Guid, PublishedItem>();

    /// <summary>List results by <see cref="ListKey"/>.</summary>
    public IReadOnlyDictionary<string, PublishedItemPage> Lists { get; init; } = new Dictionary<string, PublishedItemPage>(StringComparer.Ordinal);

    /// <summary>The address patterns of live templates by collection, for item page links.</summary>
    public IReadOnlyDictionary<Guid, string> TemplatePatterns { get; init; } = new Dictionary<Guid, string>();

    /// <summary>
    /// The page number each paginated list shows, by its page parameter: 1 when the visitor
    /// asked for none (TB-11).
    /// </summary>
    public IReadOnlyDictionary<string, int> PageNumbers { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The address being rendered, base path removed, for pager links (TB-11).</summary>
    public string ListPath { get; init; } = "/";

    /// <summary>
    /// The key of a list's result in one place on the page: the list's node id and the items of
    /// every scope around it, so a list inside a card has a result per card.
    /// </summary>
    public static string ListKey(string listId, IEnumerable<Guid> enclosingItems) =>
        listId + "@" + string.Join(',', enclosingItems.Select(id => id.ToString("N")));
}
