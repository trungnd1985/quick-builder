using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Rendering.Rendering.Blocks;

namespace QuickBuilder.Rendering.Binding;

/// <summary>A Collection list on the page, and the query that lists its items.</summary>
/// <param name="Key">Where its result waits in <see cref="ResolvedData.Lists"/>.</param>
/// <param name="Query">The query; null when the list has nothing to show, such as a field source with no item.</param>
internal sealed record PlannedList(string Key, PublishedListQuery? Query);

/// <summary>
/// Finds a page's Collection lists and plans their queries (016 research TB-09), reading the
/// list format through <see cref="ListQueryBuilder"/>, as the editor's preview does.
/// </summary>
/// <remarks>
/// Only lists outside any card are planned: a list inside a card has a result per card, and
/// those are nested scopes (US6).
/// </remarks>
internal static class ListQueries
{
    /// <summary>The page's top-level lists, in document order, at most <paramref name="maxLists"/>.</summary>
    public static List<JsonElement> TopLevel(IEnumerable<JsonElement> documents, int maxLists)
    {
        var lists = new List<JsonElement>();
        foreach (var document in documents)
        {
            if (document.TryGetProperty("root", out var root))
            {
                Visit(root);
            }
        }

        return lists;

        void Visit(JsonElement node)
        {
            if (lists.Count >= maxLists)
            {
                return;
            }

            if (node.TryGetProperty("type", out var type) && type.GetString() == BindingNodeTypes.List)
            {
                lists.Add(node);
                return;
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    Visit(child);
                }
            }
        }
    }

    /// <summary>The query parameter a paginated list reads its page from: its own, or <c>page</c> (TB-11).</summary>
    public static string PageParam(JsonElement listProps) =>
        listProps.ValueKind == JsonValueKind.Object
        && listProps.TryGetProperty("paging", out var paging) && paging.ValueKind == JsonValueKind.Object
        && paging.TryGetProperty("param", out var param) && param.GetString() is { Length: > 0 } name
            ? name
            : "page";

    /// <summary>The query of one top-level list, read through <see cref="ListQueryBuilder"/>.</summary>
    /// <param name="pageNumbers">The page each paginated list shows, by its parameter; 1 when absent.</param>
    public static PlannedList Plan(
        JsonElement list,
        PublishedItem? pageItem,
        IReadOnlyDictionary<Guid, CollectionSchemaInfo> schemas,
        IReadOnlyDictionary<string, int>? pageNumbers = null)
    {
        var id = list.TryGetProperty("id", out var nodeId) ? nodeId.GetString() ?? string.Empty : string.Empty;
        var key = ResolvedData.ListKey(id, pageItem is null ? [] : [pageItem.Id]);
        var props = list.TryGetProperty("props", out var p) ? p : default;
        IReadOnlyList<ListScope> scopes = pageItem is null ? [] : [Scope(ListScope.Page, pageItem, schemas)];
        var pageNumber = pageNumbers is not null && pageNumbers.TryGetValue(PageParam(props), out var page) ? page : 1;
        return new PlannedList(key, ListQueryBuilder.Build(props, scopes, pageNumber));
    }

    /// <summary>A published item as a scope lists can read: its published values and its schema.</summary>
    public static ListScope Scope(string name, PublishedItem item, IReadOnlyDictionary<Guid, CollectionSchemaInfo> schemas)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(schemas);

        var schema = schemas.GetValueOrDefault(item.CollectionId);
        return new ListScope(
            name,
            item.Slug,
            fieldId => BindingResolver.ReadValue(item, fieldId),
            fieldId => schema?.Field(fieldId) is { Type: FieldInfo.MultiReference, Target: { } target } ? target : null);
    }
}
