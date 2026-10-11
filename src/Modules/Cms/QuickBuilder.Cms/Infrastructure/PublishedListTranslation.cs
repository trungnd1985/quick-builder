using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Shared.Kernel.Text;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// A Collection list's filters and sort keys as EF Core over published columns (016 research
/// TB-08; binding-expression.md §5).
/// </summary>
/// <remarks>
/// <para>
/// Values are read through Npgsql's <c>jsonb</c> translation of the published document, as
/// 015's <c>ItemListQuery</c> reads the draft; reference operators go through the published
/// rows of <c>item_references</c>; text <c>contains</c> matches the folded copy in
/// <c>published_text</c>, so it ignores case and accents. No raw SQL.
/// </para>
/// <para>
/// A condition that does not fit the collection's schema today (an unknown field, an operator
/// the type does not have, a literal of the wrong kind) is broken, and a broken condition
/// makes the whole list match nothing: a list never shows more because a filter broke.
/// </para>
/// <para>
/// <b>Missing values</b> (§5): no value matches <c>empty</c> and <c>ne</c>, never <c>eq</c>,
/// <c>contains</c>, <c>gt</c> or <c>lt</c>; a boolean with no value is false. <b>Dates</b>: a
/// <c>YYYY-MM-DD</c> literal is compared with the value's day in UTC. Stored dates are
/// <c>YYYY-MM-DD</c> or UTC <c>YYYY-MM-DDTHH:MM:SSZ</c>, so text comparison orders them.
/// </para>
/// </remarks>
internal static class PublishedListTranslation
{
    /// <summary>The longest text literal a filter takes (§5).</summary>
    public const int MaxTextLiteral = 500;

    /// <summary>
    /// <paramref name="items"/> narrowed by every filter, or null when one is broken.
    /// </summary>
    public static IQueryable<PublishedItemRow>? Filter(
        IQueryable<PublishedItemRow> items,
        IQueryable<PublishedReferenceRow> references,
        ListSchema schema,
        IReadOnlyList<ListFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.Broken)
            {
                return null;
            }

            var condition = filter.Target.FieldId is { } fieldId
                ? FieldCondition(references, schema.Find(fieldId), filter)
                : BuiltInCondition(filter);
            if (condition is null)
            {
                return null;
            }

            items = items.Where(condition);
        }

        return items;
    }

    /// <summary>
    /// <paramref name="items"/> in the order of <paramref name="sorts"/>, items without a value
    /// last in either direction, then by id. Sort keys that no longer fit the schema are skipped.
    /// </summary>
    public static IOrderedQueryable<PublishedItemRow> Sort(IQueryable<PublishedItemRow> items, ListSchema schema, IReadOnlyList<ListSort> sorts)
    {
        var ordered = items.OrderBy(i => 0);
        foreach (var sort in sorts)
        {
            ordered = sort.Target.FieldId is { } fieldId
                ? SortByField(ordered, schema.Find(fieldId), sort.Descending)
                : SortByBuiltIn(ordered, sort.Target.BuiltIn, sort.Descending);
        }

        return ordered.ThenBy(i => i.Id);
    }

    private static Expression<Func<PublishedItemRow, bool>>? FieldCondition(
        IQueryable<PublishedReferenceRow> references, ListField? field, ListFilter filter)
    {
        if (field is null)
        {
            return null;
        }

        var id = field.Id;
        var op = filter.Operator;

        if (op is ListOperator.Empty or ListOperator.NotEmpty)
        {
            if (filter.Value is { ValueKind: not JsonValueKind.Null })
            {
                return null;
            }

            // ->> gives NULL for a missing key or a JSON null, and the text of anything else.
            return op == ListOperator.Empty
                ? i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == null
                    || i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == ""
                    || i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == "[]"
                : i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != null
                    && i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != ""
                    && i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != "[]";
        }

        if (filter.Value is not { } value)
        {
            return null;
        }

        switch (field.Type)
        {
            case ListFieldTypes.Text when value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= MaxTextLiteral:
                {
                    var text = value.GetString()!;
                    switch (op)
                    {
                        case ListOperator.Eq:
                            return i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == text;
                        case ListOperator.Ne:
                            return i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == null
                                || i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != text;
                        case ListOperator.Contains:
                            var pattern = SearchText.LikePattern(text);
                            return i => EF.Functions.Like(i.PublishedText!.RootElement.GetProperty(id).GetString()!, pattern, "\\");
                        default:
                            return null;
                    }
                }

            case ListFieldTypes.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number):
                return op switch
                {
                    ListOperator.Eq => i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != null
                        && i.Published!.RootElement.GetProperty("values").GetProperty(id).GetDecimal() == number,
                    ListOperator.Ne => i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == null
                        || i.Published!.RootElement.GetProperty("values").GetProperty(id).GetDecimal() != number,
                    ListOperator.Gt => i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != null
                        && i.Published!.RootElement.GetProperty("values").GetProperty(id).GetDecimal() > number,
                    ListOperator.Lt => i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != null
                        && i.Published!.RootElement.GetProperty("values").GetProperty(id).GetDecimal() < number,
                    _ => null,
                };

            case ListFieldTypes.Date when Day(value) is { } day:
                {
                    var start = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var next = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return op switch
                    {
                        // [start, next): the day itself, with or without a time.
                        ListOperator.Eq => i => string.Compare(i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString(), start) >= 0
                            && string.Compare(i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString(), next) < 0,
                        ListOperator.Gt => i => string.Compare(i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString(), next) >= 0,
                        ListOperator.Lt => i => string.Compare(i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString(), start) < 0,
                        _ => null,
                    };
                }

            case ListFieldTypes.Boolean when op == ListOperator.Eq && value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                // ->> gives the text "true" or "false"; no value counts as false (014 FR-010).
                return value.ValueKind == JsonValueKind.True
                    ? i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == "true"
                    : i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == null
                        || i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == "false";

            case ListFieldTypes.Option when value.ValueKind == JsonValueKind.String && field.Options.Contains(value.GetString()!):
                {
                    var option = value.GetString()!;
                    return op switch
                    {
                        ListOperator.Eq => i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == option,
                        ListOperator.Ne => i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == null
                            || i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() != option,
                        _ => null,
                    };
                }

            case ListFieldTypes.Reference when value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var target):
                return op switch
                {
                    ListOperator.Eq => i => references.Any(r => r.SourceItemId == i.Id && r.FieldId == id && r.TargetItemId == target),
                    ListOperator.Ne => i => !references.Any(r => r.SourceItemId == i.Id && r.FieldId == id && r.TargetItemId == target),
                    _ => null,
                };

            case ListFieldTypes.MultiReference when op == ListOperator.Contains
                && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var member):
                return i => references.Any(r => r.SourceItemId == i.Id && r.FieldId == id && r.TargetItemId == member);

            default:
                // Rich text, image and file have only empty and notEmpty; anything else misfits.
                return null;
        }
    }

    private static Expression<Func<PublishedItemRow, bool>>? BuiltInCondition(ListFilter filter)
    {
        if (filter.Value is not { } value)
        {
            return null;
        }

        switch (filter.Target.BuiltIn)
        {
            case ListBuiltIn.Slug when value.ValueKind == JsonValueKind.String:
                {
                    var slug = value.GetString()!;
                    return filter.Operator switch
                    {
                        ListOperator.Eq => i => i.PublishedSlug == slug,
                        ListOperator.Ne => i => i.PublishedSlug != slug,
                        _ => null,
                    };
                }

            case ListBuiltIn.FirstPublishedAt or ListBuiltIn.LastPublishedAt when Day(value) is { } day:
                {
                    var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                    var next = start.AddDays(1);
                    var first = filter.Target.BuiltIn == ListBuiltIn.FirstPublishedAt;
                    return filter.Operator switch
                    {
                        ListOperator.Eq when first => i => i.FirstPublishedAtUtc >= start && i.FirstPublishedAtUtc < next,
                        ListOperator.Gt when first => i => i.FirstPublishedAtUtc >= next,
                        ListOperator.Lt when first => i => i.FirstPublishedAtUtc < start,
                        ListOperator.Eq => i => i.LastPublishedAtUtc >= start && i.LastPublishedAtUtc < next,
                        ListOperator.Gt => i => i.LastPublishedAtUtc >= next,
                        ListOperator.Lt => i => i.LastPublishedAtUtc < start,
                        _ => null,
                    };
                }

            default:
                return null;
        }
    }

    private static IOrderedQueryable<PublishedItemRow> SortByField(IOrderedQueryable<PublishedItemRow> items, ListField? field, bool descending)
    {
        if (field is null)
        {
            return items;
        }

        var id = field.Id;
        var valuesLast = items.ThenBy(i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString() == null);
        return field.Type switch
        {
            ListFieldTypes.Number => By(valuesLast, i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetDecimal(), descending),
            ListFieldTypes.Text or ListFieldTypes.Date or ListFieldTypes.Boolean or ListFieldTypes.Option =>
                By(valuesLast, i => i.Published!.RootElement.GetProperty("values").GetProperty(id).GetString(), descending),
            _ => items,
        };
    }

    private static IOrderedQueryable<PublishedItemRow> SortByBuiltIn(IOrderedQueryable<PublishedItemRow> items, ListBuiltIn? builtIn, bool descending) =>
        builtIn switch
        {
            ListBuiltIn.Slug => By(items, i => i.PublishedSlug, descending),
            ListBuiltIn.FirstPublishedAt => By(items, i => i.FirstPublishedAtUtc, descending),
            ListBuiltIn.LastPublishedAt => By(items, i => i.LastPublishedAtUtc, descending),
            _ => items,
        };

    private static IOrderedQueryable<PublishedItemRow> By<TKey>(
        IOrderedQueryable<PublishedItemRow> items, Expression<Func<PublishedItemRow, TKey>> key, bool descending) =>
        descending ? items.ThenByDescending(key) : items.ThenBy(key);

    /// <summary>A <c>YYYY-MM-DD</c> literal as a day; null when it is not one.</summary>
    private static DateOnly? Day(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
}

/// <summary>The field types a list reads (collection-schema.md §2).</summary>
internal static class ListFieldTypes
{
    public const string Text = "text";
    public const string RichText = "richText";
    public const string Number = "number";
    public const string Date = "date";
    public const string Boolean = "boolean";
    public const string Option = "option";
    public const string Image = "image";
    public const string File = "file";
    public const string Reference = "reference";
    public const string MultiReference = "multiReference";
}

/// <summary>One field of a collection, as a list query needs it.</summary>
internal sealed record ListField(string Id, string Type, IReadOnlySet<string> Options);

/// <summary>A collection's current fields, read from its schema document.</summary>
internal sealed class ListSchema(IReadOnlyDictionary<string, ListField> fields)
{
    public static ListSchema Empty { get; } = new(new Dictionary<string, ListField>());

    public ListField? Find(string fieldId) => fields.GetValueOrDefault(fieldId);

    public static ListSchema Read(JsonElement schema)
    {
        var fields = new Dictionary<string, ListField>(StringComparer.Ordinal);
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("fields", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in list.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object
                    || !field.TryGetProperty("id", out var id) || id.GetString() is not { } fieldId
                    || !field.TryGetProperty("type", out var type) || type.GetString() is not { } fieldType)
                {
                    continue;
                }

                var options = new HashSet<string>(StringComparer.Ordinal);
                if (field.TryGetProperty("options", out var optionList) && optionList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in optionList.EnumerateArray())
                    {
                        if (option.ValueKind == JsonValueKind.Object && option.TryGetProperty("id", out var optionId) && optionId.GetString() is { } value)
                        {
                            options.Add(value);
                        }
                    }
                }

                fields[fieldId] = new ListField(fieldId, fieldType, options);
            }
        }

        return new ListSchema(fields);
    }
}
