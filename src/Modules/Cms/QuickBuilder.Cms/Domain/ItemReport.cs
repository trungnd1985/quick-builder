using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickBuilder.Cms.Domain;

/// <summary>The problem codes of an item report (015 research IT-05, data-model §2).</summary>
internal static class ItemProblems
{
    // Malformed: never stored (FR-013).
    public const string Malformed = "malformed";
    public const string UnknownField = "unknown_field";
    public const string UnknownOption = "unknown_option";
    public const string ReferenceMissing = "reference_missing";
    public const string ReferenceWrongCollection = "reference_wrong_collection";
    public const string ReferenceSelf = "reference_self";
    public const string DuplicateReference = "duplicate_reference";
    public const string TooManyReferences = "too_many_references";
    public const string InvalidSlug = "invalid_slug";

    // Reported: stored in a draft, refused at publish (FR-014, FR-020).
    public const string MissingRequired = "missing_required";
    public const string TooLong = "too_long";
    public const string NotSingleLine = "not_single_line";
    public const string OutOfRange = "out_of_range";
    public const string TooManyDecimals = "too_many_decimals";
    public const string FileKind = "file_kind";
    public const string MediaMissing = "media_missing";

    /// <summary>A scheduled action failed after the item last changed (FR-026); field id <c>schedule</c>.</summary>
    public const string ScheduleFailed = "schedule_failed";

    private static readonly HashSet<string> MalformedCodes =
    [
        Malformed, UnknownField, UnknownOption, ReferenceMissing, ReferenceWrongCollection,
        ReferenceSelf, DuplicateReference, TooManyReferences, InvalidSlug,
    ];

    /// <summary>Whether a value with this problem may not be stored at all.</summary>
    public static bool IsMalformed(string code) => MalformedCodes.Contains(code);
}

/// <summary>One thing wrong with one field of an item, or with its slug (field id <c>slug</c>).</summary>
/// <param name="Limit">The setting broken, when there is one: the maximum length, the bound, the decimals.</param>
internal sealed record ItemProblem(string FieldId, string Problem, decimal? Limit = null);

/// <summary>
/// Everything wrong with one copy of an item, measured against its schema (015 research IT-05).
/// </summary>
/// <remarks>
/// One report decides everything: a draft save refuses it when <see cref="HasMalformed"/>,
/// a publish when it is not <see cref="IsEmpty"/>, and the item's <c>attention</c> column holds
/// it. It never holds values, only field ids, codes and limits.
/// </remarks>
internal sealed class ItemReport
{
    /// <summary>The pseudo field id of a problem with the slug.</summary>
    public const string SlugField = "slug";

    /// <summary>The pseudo field id of a failed scheduled action.</summary>
    public const string ScheduleField = "schedule";

    public static ItemReport Clean { get; } = new([]);

    public ItemReport(IReadOnlyList<ItemProblem> problems)
    {
        Problems = problems;
    }

    public IReadOnlyList<ItemProblem> Problems { get; }

    public bool IsEmpty => Problems.Count == 0;

    public bool HasMalformed => Problems.Any(p => ItemProblems.IsMalformed(p.Problem));

    /// <summary>The report without its malformed problems: what a stored draft can still need attention for.</summary>
    public ItemReport Reported => new(Problems.Where(p => !ItemProblems.IsMalformed(p.Problem)).ToList());

    /// <summary>This report with <paramref name="problem"/> added.</summary>
    public ItemReport With(ItemProblem problem) => new([.. Problems, problem]);

    public JsonObject Write()
    {
        var problems = new JsonArray();
        foreach (var problem in Problems)
        {
            var entry = new JsonObject { ["fieldId"] = problem.FieldId, ["problem"] = problem.Problem };
            if (problem.Limit is { } limit)
            {
                entry["limit"] = limit;
            }

            problems.Add(entry);
        }

        return new JsonObject { ["problems"] = problems };
    }

    /// <summary>The <c>attention</c> column: null when there is nothing to report.</summary>
    public JsonDocument? ToAttention() => IsEmpty ? null : JsonDocument.Parse(Write().ToJsonString());

    public static ItemReport Read(JsonDocument? attention)
    {
        if (attention is null)
        {
            return Clean;
        }

        var problems = attention.RootElement.GetProperty("problems").EnumerateArray()
            .Select(p => new ItemProblem(
                p.GetProperty("fieldId").GetString()!,
                p.GetProperty("problem").GetString()!,
                p.TryGetProperty("limit", out var limit) && limit.ValueKind == JsonValueKind.Number ? limit.GetDecimal() : null))
            .ToList();
        return new ItemReport(problems);
    }
}
