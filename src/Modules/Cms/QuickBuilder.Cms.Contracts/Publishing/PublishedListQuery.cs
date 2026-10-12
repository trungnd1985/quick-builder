using System.Text.Json;

namespace QuickBuilder.Cms.Contracts.Publishing;

/// <summary>
/// A Collection list's query over published items: source, filters, sort keys and the page
/// wanted (binding-expression.md §5, research TB-08).
/// </summary>
/// <remarks>
/// Built by the renderer from a list block, with every scope value already resolved to a
/// literal, so Cms sees only literals. A condition whose field or operand no longer fits the
/// collection's schema matches nothing; a broken filter never widens a list.
/// </remarks>
/// <param name="CollectionId">The collection whose items are listed.</param>
/// <param name="ItemIds">
/// For a list sourced from a multi-reference field: the referenced ids, in the field's order,
/// which is also the order when no sort key is given. Null for a whole collection.
/// </param>
/// <param name="Filters">Conditions that must all hold.</param>
/// <param name="Sorts">Sort keys, most significant first; the item id breaks ties.</param>
/// <param name="Offset">How many matching items to skip.</param>
/// <param name="Count">How many items to return at most.</param>
public sealed record PublishedListQuery(
    Guid CollectionId,
    IReadOnlyList<Guid>? ItemIds,
    IReadOnlyList<ListFilter> Filters,
    IReadOnlyList<ListSort> Sorts,
    int Offset,
    int Count);

/// <summary>What a filter or sort key reads: a field by id, or a built-in item value.</summary>
/// <param name="FieldId">A field id such as <c>f3</c>; null for a built-in value.</param>
/// <param name="BuiltIn">A built-in value when <paramref name="FieldId"/> is null.</param>
public sealed record ListTarget(string? FieldId, ListBuiltIn? BuiltIn)
{
    public static ListTarget Field(string fieldId) => new(fieldId, null);

    public static ListTarget Item(ListBuiltIn builtIn) => new(null, builtIn);
}

/// <summary>The built-in item values a list can filter and sort on.</summary>
public enum ListBuiltIn
{
    Slug,
    FirstPublishedAt,
    LastPublishedAt,
}

/// <summary>A filter operator (binding-expression.md §5).</summary>
public enum ListOperator
{
    Eq,
    Ne,
    Contains,
    Gt,
    Lt,
    Empty,
    NotEmpty,
}

/// <summary>
/// One condition. <see cref="Value"/> is a literal (string, number or boolean), or null for
/// <c>empty</c> and <c>notEmpty</c>. A condition the renderer could not resolve, such as a scope
/// value that is missing, is sent with <see cref="Broken"/> set and matches nothing.
/// </summary>
public sealed record ListFilter(ListTarget Target, ListOperator Operator, JsonElement? Value, bool Broken = false)
{
    public static ListFilter Never { get; } = new(ListTarget.Item(ListBuiltIn.Slug), ListOperator.Eq, null, Broken: true);
}

/// <summary>One sort key.</summary>
public sealed record ListSort(ListTarget Target, bool Descending);
