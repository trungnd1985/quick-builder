using System.Text.Json;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Rendering.Rendering.Blocks;

namespace QuickBuilder.Rendering.Binding;

/// <summary>A Collection list on the page, and the query that lists its items.</summary>
/// <param name="Key">Where its result waits in <see cref="ResolvedData.Lists"/>.</param>
/// <param name="Query">The query; null when the list has nothing to show, such as a field source with no item.</param>
internal sealed record PlannedList(string Key, PublishedListQuery? Query);

/// <summary>
/// Turns Collection list blocks into published-item queries (016 research TB-09;
/// binding-expression.md §5). Pure: the page's item and schemas come in, queries go out.
/// </summary>
/// <remarks>
/// <para>
/// Every scope value is resolved here, so Cms sees only literals: a filter reading a field of
/// the page's item takes that value, <c>{ "current": true }</c> becomes "slug is not the page
/// item's", and a value that cannot be read is sent as a broken condition, which matches
/// nothing (a list never widens because a filter broke).
/// </para>
/// <para>
/// Only lists outside any card are planned: a list inside a card has a result per card, and
/// those are nested scopes (US6).
/// </para>
/// </remarks>
internal static class ListQueries
{
    /// <summary>How many items a list without paging shows when it states no limit (§5).</summary>
    public const int DefaultLimit = 12;

    public const int MaxLimit = 100;

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

    /// <summary>The query of one top-level list.</summary>
    /// <param name="pageNumber">The page a paginated list shows, from 1.</param>
    public static PlannedList Plan(
        JsonElement list, PublishedItem? pageItem, IReadOnlyDictionary<Guid, CollectionSchemaInfo> schemas, int pageNumber = 1)
    {
        var id = list.TryGetProperty("id", out var nodeId) ? nodeId.GetString() ?? string.Empty : string.Empty;
        var key = ResolvedData.ListKey(id, pageItem is null ? [] : [pageItem.Id]);
        var props = list.TryGetProperty("props", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
        {
            return new PlannedList(key, null);
        }

        Guid collectionId;
        IReadOnlyList<Guid>? itemIds = null;
        if (source.TryGetProperty("collectionId", out var collection) && Guid.TryParse(collection.GetString(), out var direct))
        {
            collectionId = direct;
        }
        else if (source.TryGetProperty("field", out var field) && field.GetString() is { } fieldId
            && Scope(source) is null or ScopeStack.Page
            && pageItem is not null
            && schemas.GetValueOrDefault(pageItem.CollectionId)?.Field(fieldId) is { Type: FieldInfo.MultiReference, Target: { } target })
        {
            // The referenced items, in the field's order (§5).
            collectionId = target;
            itemIds = BindingResolver.ReadValue(pageItem, fieldId) is { ValueKind: JsonValueKind.Array } ids
                ? [.. ids.EnumerateArray().Select(v => Guid.TryParse(v.GetString(), out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)]
                : [];
            if (itemIds.Count == 0)
            {
                return new PlannedList(key, null);
            }
        }
        else
        {
            return new PlannedList(key, null);
        }

        var filters = props.TryGetProperty("filters", out var filterList) && filterList.ValueKind == JsonValueKind.Array
            ? filterList.EnumerateArray().Select(f => Filter(f, pageItem)).ToList()
            : [];
        var sorts = props.TryGetProperty("sort", out var sortList) && sortList.ValueKind == JsonValueKind.Array
            ? sortList.EnumerateArray().Select(Sort).OfType<ListSort>().ToList()
            : [];

        int offset;
        int count;
        if (props.TryGetProperty("paging", out var paging) && paging.ValueKind == JsonValueKind.Object)
        {
            count = Math.Clamp(paging.TryGetProperty("pageSize", out var size) && size.TryGetInt32(out var s) ? s : DefaultLimit, 1, MaxLimit);
            offset = (Math.Max(pageNumber, 1) - 1) * count;
        }
        else
        {
            count = Math.Clamp(props.TryGetProperty("limit", out var limit) && limit.TryGetInt32(out var l) ? l : DefaultLimit, 1, MaxLimit);
            offset = 0;
        }

        return new PlannedList(key, new PublishedListQuery(collectionId, itemIds, filters, sorts, offset, count));
    }

    private static ListFilter Filter(JsonElement filter, PublishedItem? pageItem)
    {
        if (filter.ValueKind != JsonValueKind.Object || Target(filter) is not { } target
            || !filter.TryGetProperty("op", out var op) || Operator(op.GetString()) is not { } listOperator)
        {
            return ListFilter.Never;
        }

        if (!filter.TryGetProperty("value", out var value))
        {
            return new ListFilter(target, listOperator, null);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return new ListFilter(target, listOperator, value);
        }

        // { "current": true }: "not that item", for a list of the page item's collection.
        if (value.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.True)
        {
            return listOperator == ListOperator.Ne && pageItem is not null && Scope(value) is null or ScopeStack.Page
                ? new ListFilter(ListTarget.Item(ListBuiltIn.Slug), ListOperator.Ne, JsonSerializer.SerializeToElement(pageItem.Slug))
                : ListFilter.Never;
        }

        // { "field": id }: the page item's value. A missing scope or value matches nothing.
        if (value.TryGetProperty("field", out var field) && field.GetString() is { } fieldId
            && Scope(value) is null or ScopeStack.Page
            && pageItem is not null
            && BindingResolver.ReadValue(pageItem, fieldId) is { ValueKind: JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } read
            && !(read.ValueKind == JsonValueKind.String && read.GetString()!.Length == 0))
        {
            return new ListFilter(target, listOperator, read);
        }

        return ListFilter.Never;
    }

    private static ListSort? Sort(JsonElement sort) =>
        sort.ValueKind == JsonValueKind.Object && Target(sort) is { } target
            ? new ListSort(target, sort.TryGetProperty("dir", out var dir) && dir.GetString() == "desc")
            : null;

    private static ListTarget? Target(JsonElement element)
    {
        if (element.TryGetProperty("field", out var field) && field.GetString() is { } fieldId)
        {
            return ListTarget.Field(fieldId);
        }

        return element.TryGetProperty("item", out var item)
            ? item.GetString() switch
            {
                "slug" => ListTarget.Item(ListBuiltIn.Slug),
                "firstPublishedAt" => ListTarget.Item(ListBuiltIn.FirstPublishedAt),
                "lastPublishedAt" => ListTarget.Item(ListBuiltIn.LastPublishedAt),
                _ => null,
            }
            : null;
    }

    private static ListOperator? Operator(string? op) => op switch
    {
        "eq" => ListOperator.Eq,
        "ne" => ListOperator.Ne,
        "contains" => ListOperator.Contains,
        "gt" => ListOperator.Gt,
        "lt" => ListOperator.Lt,
        "empty" => ListOperator.Empty,
        "notEmpty" => ListOperator.NotEmpty,
        _ => null,
    };

    private static string? Scope(JsonElement element) =>
        element.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String ? scope.GetString() : null;
}
