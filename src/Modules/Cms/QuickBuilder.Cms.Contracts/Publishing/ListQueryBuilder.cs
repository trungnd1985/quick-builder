using System.Text.Json;

namespace QuickBuilder.Cms.Contracts.Publishing;

/// <summary>
/// One binding scope a list can read from (binding-expression.md §3): the page's item, or the
/// item of an enclosing list's card.
/// </summary>
/// <param name="Name"><c>page</c>, or the enclosing list's node id.</param>
/// <param name="Slug">The item's slug, for <c>{ "current": true }</c>.</param>
/// <param name="Read">A field's value of the item; null when it has none.</param>
/// <param name="MultiReferenceTarget">A multi-reference field's target collection; null for any other field.</param>
public sealed record ListScope(string Name, string Slug, Func<string, JsonElement?> Read, Func<string, Guid?> MultiReferenceTarget)
{
    public const string Page = "page";
}

/// <summary>
/// Turns a Collection list block's props into a <see cref="PublishedListQuery"/> (016 research
/// TB-09, TB-14; binding-expression.md §5): the one reading of the list format, for the public
/// renderer and the editor's preview alike.
/// </summary>
/// <remarks>
/// Every scope value is resolved here, so Cms sees only literals: a filter reading a scope's
/// field takes that value, <c>{ "current": true }</c> becomes "slug is not that item's", and a
/// value that cannot be read is sent as a broken condition, which matches nothing (a list never
/// widens because a filter broke).
/// </remarks>
public static class ListQueryBuilder
{
    /// <summary>How many items a list without paging shows when it states no limit (§5).</summary>
    public const int DefaultLimit = 12;

    public const int MaxLimit = 100;

    /// <summary>
    /// The query of a list with <paramref name="props"/>, inside <paramref name="scopes"/>
    /// (outermost first); null when the list has nothing to show, such as a field source whose
    /// scope or value is missing.
    /// </summary>
    /// <param name="pageNumber">The page a paginated list shows, from 1.</param>
    public static PublishedListQuery? Build(JsonElement props, IReadOnlyList<ListScope> scopes, int pageNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        if (props.ValueKind != JsonValueKind.Object || !props.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Guid collectionId;
        IReadOnlyList<Guid>? itemIds = null;
        if (source.TryGetProperty("collectionId", out var collection) && Guid.TryParse(collection.GetString(), out var direct))
        {
            collectionId = direct;
        }
        else if (source.TryGetProperty("field", out var field) && field.GetString() is { } fieldId
            && Find(scopes, Scope(source)) is { } scope
            && scope.MultiReferenceTarget(fieldId) is { } target)
        {
            // The referenced items, in the field's order (§5).
            collectionId = target;
            itemIds = scope.Read(fieldId) is { ValueKind: JsonValueKind.Array } ids
                ? [.. ids.EnumerateArray().Select(v => Guid.TryParse(v.GetString(), out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)]
                : [];
            if (itemIds.Count == 0)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        var filters = props.TryGetProperty("filters", out var filterList) && filterList.ValueKind == JsonValueKind.Array
            ? filterList.EnumerateArray().Select(f => Filter(f, scopes)).ToList()
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

        return new PublishedListQuery(collectionId, itemIds, filters, sorts, offset, count);
    }

    /// <summary>The scope a reader means: <paramref name="name"/>'s, or the innermost when it names none.</summary>
    public static ListScope? Find(IReadOnlyList<ListScope> scopes, string? name)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        if (name is null)
        {
            return scopes.Count > 0 ? scopes[^1] : null;
        }

        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            if (string.Equals(scopes[i].Name, name, StringComparison.Ordinal))
            {
                return scopes[i];
            }
        }

        return null;
    }

    private static ListFilter Filter(JsonElement filter, IReadOnlyList<ListScope> scopes)
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

        // { "current": true }: "not that item", for a list of the scope item's collection.
        if (value.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.True)
        {
            return listOperator == ListOperator.Ne && Find(scopes, Scope(value)) is { } item
                ? new ListFilter(ListTarget.Item(ListBuiltIn.Slug), ListOperator.Ne, JsonSerializer.SerializeToElement(item.Slug))
                : ListFilter.Never;
        }

        // { "field": id }: the scope item's value. A missing scope or value matches nothing.
        if (value.TryGetProperty("field", out var field) && field.GetString() is { } fieldId
            && Find(scopes, Scope(value))?.Read(fieldId) is { ValueKind: JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } read
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
