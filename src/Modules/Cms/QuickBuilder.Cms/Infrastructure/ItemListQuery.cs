using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Text;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>The list query of a collection's items, as the request gives it (015 FR-028, FR-029).</summary>
internal sealed record ItemListRequest(
    string? Search,
    string? Status,
    bool? Attention,
    string? Scheduled,
    IReadOnlyDictionary<string, string> Fields,
    string? Sort,
    string? Direction,
    int? Page,
    int? PageSize);

/// <summary>One page of the list, and how many items match.</summary>
internal sealed record ItemPage(IReadOnlyList<ItemRow> Rows, int Total, int Page, int PageSize);

/// <summary>
/// Search, filters, sorts and numbered pages over a collection's items, in EF Core only (015
/// research IT-13, IT-14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Search</b> matches name or slug through the stored, normalised <c>search_text</c>, ignoring
/// case and accents, the term taken literally (clarification Q5). <b>Filters</b> on field values
/// and <b>sorts</b> on number and date fields read the draft values through Npgsql's JSON
/// translation, so no raw SQL is needed. Items without a value come last in both directions, and
/// the id breaks ties so a page is the same on reload.
/// </para>
/// <para>
/// Pages are numbered (<c>OFFSET</c>/<c>LIMIT</c>), with the total: FR-028 asks for page numbers,
/// and at the tested 10,000 items the offset is cheap.
/// </para>
/// </remarks>
internal sealed class ItemListQuery(CmsDbContext database)
{
    public static readonly int[] PageSizes = [25, 50, 100];

    private static readonly string[] ColumnSorts = ["name", "slug", "status", "createdAt", "updatedAt", "publishedAt"];

    public async Task<Result<ItemPage>> RunAsync(Guid siteId, Guid collectionId, CollectionSchemaDocument schema, ItemListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(request);

        var pageSize = request.PageSize ?? PageSizes[0];
        var page = request.Page ?? 1;
        if (!PageSizes.Contains(pageSize))
        {
            return Invalid("pageSize");
        }

        if (page < 1)
        {
            return Invalid("page");
        }

        var items = database.ForSite<Item>(siteId).AsNoTracking().Where(i => i.CollectionId == collectionId);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        if (search is not null)
        {
            if (search.Length > SearchText.MaxTermLength)
            {
                return Invalid("q");
            }

            var pattern = SearchText.LikePattern(search);
            items = items.Where(i => EF.Functions.Like(i.SearchText, pattern, "\\"));
        }

        if (request.Status is { } status)
        {
            if (!ItemStatuses.IsKnown(status))
            {
                return Invalid("status");
            }

            items = items.Where(i => i.Status == status);
        }

        if (request.Attention == true)
        {
            items = items.Where(i => i.Attention != null);
        }

        if (request.Scheduled is { } scheduled)
        {
            if (scheduled is not ("any" or ItemScheduleActions.Publish or ItemScheduleActions.Unpublish))
            {
                return Invalid("scheduled");
            }

            var pending = database.ForSite<ItemSchedule>(siteId)
                .Where(s => s.State == ItemScheduleStates.Pending && (scheduled == "any" || s.Action == scheduled));
            items = items.Where(i => pending.Any(s => s.ItemId == i.Id));
        }

        foreach (var (fieldId, value) in request.Fields)
        {
            var filtered = Filter(items, schema.Find(fieldId), value);
            if (filtered is null)
            {
                return Invalid($"field[{fieldId}]");
            }

            items = filtered;
        }

        var descending = request.Direction switch
        {
            null or "desc" => true,
            "asc" => false,
            _ => (bool?)null,
        };
        if (descending is null)
        {
            return Invalid("dir");
        }

        var sorted = Sort(items, schema, request.Sort ?? "updatedAt", descending.Value);
        if (sorted is null)
        {
            return Invalid("sort");
        }

        var total = await items.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new ItemRow(i.Id, i.Name, i.Slug, i.Status, i.Attention != null, i.CreatedAtUtc, i.UpdatedAtUtc, i.UpdatedBy, i.PublishedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new ItemPage(rows, total, page, pageSize));
    }

    /// <summary>Items whose draft holds <paramref name="value"/> in the field; null when the field cannot be filtered so.</summary>
    private static IQueryable<Item>? Filter(IQueryable<Item> items, Field? field, string value)
    {
        if (field is null)
        {
            return null;
        }

        var id = field.Id;
        switch (field.Type)
        {
            case FieldType.Option when field.FindOption(value) is not null:
            case FieldType.Reference when Guid.TryParse(value, out _):
                return items.Where(i => i.Draft.RootElement.GetProperty("values").GetProperty(id).GetString() == value);
            case FieldType.MultiReference when Guid.TryParse(value, out var target):
                var contained = $"[\"{target:D}\"]";
                return items.Where(i => EF.Functions.JsonContains(i.Draft.RootElement.GetProperty("values").GetProperty(id), contained));
            case FieldType.Boolean when value == "true":
                return items.Where(i => i.Draft.RootElement.GetProperty("values").GetProperty(id).GetString() == "true");
            case FieldType.Boolean when value == "false":
                // A boolean with no value counts as false (014 FR-010).
                return items.Where(i => i.Draft.RootElement.GetProperty("values").GetProperty(id).GetString() == null
                    || i.Draft.RootElement.GetProperty("values").GetProperty(id).GetString() == "false");
            default:
                return null;
        }
    }

    /// <summary>The items in the requested order, missing values last, then by id; null for an unknown sort.</summary>
    private static IOrderedQueryable<Item>? Sort(IQueryable<Item> items, CollectionSchemaDocument schema, string sort, bool descending)
    {
        if (ColumnSorts.Contains(sort))
        {
            return sort switch
            {
                "name" => By(items.OrderBy(i => i.Name == null), i => i.Name!.Substring(0, 256), descending),
                "slug" => By(items.OrderBy(i => 0), i => i.Slug, descending),
                "status" => By(items.OrderBy(i => 0), i => i.Status, descending),
                "createdAt" => By(items.OrderBy(i => 0), i => i.CreatedAtUtc, descending),
                "updatedAt" => By(items.OrderBy(i => 0), i => i.UpdatedAtUtc, descending),
                _ => By(items.OrderBy(i => i.PublishedAtUtc == null), i => i.PublishedAtUtc, descending),
            };
        }

        if (!sort.StartsWith("field.", StringComparison.Ordinal) || schema.Find(sort["field.".Length..]) is not { } field)
        {
            return null;
        }

        var id = field.Id;
        var missingLast = items.OrderBy(i => i.Draft.RootElement.GetProperty("values").GetProperty(id).GetString() == null);
        return field.Type switch
        {
            FieldType.Number => By(missingLast, i => i.Draft.RootElement.GetProperty("values").GetProperty(id).GetDecimal(), descending),

            // Contract dates sort correctly as text: YYYY-MM-DD, or UTC YYYY-MM-DDTHH:MM:SSZ.
            FieldType.Date => By(missingLast, i => i.Draft.RootElement.GetProperty("values").GetProperty(id).GetString(), descending),
            _ => null,
        };
    }

    private static IOrderedQueryable<Item> By<TKey>(IOrderedQueryable<Item> first, Expression<Func<Item, TKey>> key, bool descending) =>
        (descending ? first.ThenByDescending(key) : first.ThenBy(key)).ThenBy(i => i.Id);

    private static Result<ItemPage> Invalid(string parameter) => Result.Failure<ItemPage>(CmsErrors.InvalidQuery(parameter));
}
