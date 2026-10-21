using System.Text.Json;

namespace QuickBuilder.Pages.Domain.Forms;

/// <summary>
/// What a form must be before it reaches visitors (page-schema.md §3 rule 28; 018 research
/// FS-04). A draft may break these while someone works on it; publishing refuses it.
/// </summary>
/// <remarks>
/// Pure, over a <c>form</c> node that passed the schema, so shapes are trusted. The admin's
/// <c>formSetup.ts</c> reports the same problems in the same order; the shared fixture
/// <c>tests/fixtures/forms/setup-rules.json</c> holds both to it. Rule 28.6 needs the site, so
/// this only names the pages (<see cref="LinkedPages"/>) and the publish guard checks them.
/// </remarks>
internal static class FormPublishRules
{
    public const string NoFields = "no_fields";
    public const string DuplicateLabel = "duplicate_label";
    public const string NoOptions = "no_options";
    public const string DuplicateOption = "duplicate_option";
    public const string MinAboveMax = "min_above_max";
    public const string ReplyFieldInvalid = "reply_field_invalid";

    /// <summary>Every problem of one form: the form's first, then each field in order, then the reply field.</summary>
    public static IReadOnlyList<FormSetupProblem> Check(JsonElement formNode)
    {
        var props = formNode.GetProperty("props");
        var fields = props.GetProperty("fields");
        var problems = new List<FormSetupProblem>();

        if (fields.GetArrayLength() == 0)
        {
            problems.Add(new FormSetupProblem(NoFields, null));
        }

        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields.EnumerateArray())
        {
            var id = field.GetProperty("id").GetString()!;

            if (!labels.Add(field.GetProperty("label").GetString()!.Trim().ToLowerInvariant()))
            {
                problems.Add(new FormSetupProblem(DuplicateLabel, id));
            }

            var type = field.GetProperty("type").GetString()!;
            var optionCount = 0;
            if (type is "select" or "radio" or "checkboxes")
            {
                if (!field.TryGetProperty("options", out var options) || options.GetArrayLength() == 0)
                {
                    problems.Add(new FormSetupProblem(NoOptions, id));
                }
                else
                {
                    optionCount = options.GetArrayLength();
                    var values = new HashSet<string>(StringComparer.Ordinal);
                    if (options.EnumerateArray().Any(o => !values.Add(o.GetProperty("value").GetString()!)))
                    {
                        problems.Add(new FormSetupProblem(DuplicateOption, id));
                    }
                }
            }

            if (IsMinAboveMax(field, type, optionCount))
            {
                problems.Add(new FormSetupProblem(MinAboveMax, id));
            }
        }

        if (props.TryGetProperty("notify", out var notify)
            && notify.TryGetProperty("replyFieldId", out var reply)
            && !ReplyFieldIsEmail(fields, reply.GetString()!, notify))
        {
            problems.Add(new FormSetupProblem(ReplyFieldInvalid, reply.GetString()));
        }

        return problems;
    }

    /// <summary>
    /// The pages rule 28.6 needs live: the redirect's page first, then each consent link once,
    /// in field order.
    /// </summary>
    public static IReadOnlyList<Guid> LinkedPages(JsonElement formNode)
    {
        var props = formNode.GetProperty("props");
        var pages = new List<Guid>();

        if (props.TryGetProperty("redirect", out var redirect) && redirect.TryGetProperty("pageId", out var redirectPage))
        {
            pages.Add(redirectPage.GetGuid());
        }

        foreach (var field in props.GetProperty("fields").EnumerateArray())
        {
            if (field.TryGetProperty("linkPageId", out var link) && !pages.Contains(link.GetGuid()))
            {
                pages.Add(link.GetGuid());
            }
        }

        return pages;
    }

    private static bool IsMinAboveMax(JsonElement field, string type, int optionCount)
    {
        switch (type)
        {
            case "text" or "textarea" or "email" or "phone":
                var maxLength = Int(field, "maxLength") ?? DefaultMaxLength(type);
                return Int(field, "minLength") is { } minLength && minLength > maxLength;

            case "number":
                return field.TryGetProperty("min", out var min) && field.TryGetProperty("max", out var max)
                    && min.GetDecimal() > max.GetDecimal();

            case "date":
                // ISO dates compare as strings.
                return field.TryGetProperty("earliest", out var earliest) && field.TryGetProperty("latest", out var latest)
                    && string.CompareOrdinal(earliest.GetString(), latest.GetString()) > 0;

            case "checkboxes":
                var minSelected = Int(field, "minSelected");
                var maxSelected = Int(field, "maxSelected");
                return (minSelected is { } low && maxSelected is { } high && low > high)
                    || (optionCount > 0 && (maxSelected > optionCount || minSelected > optionCount));

            default:
                return false;
        }
    }

    private static bool ReplyFieldIsEmail(JsonElement fields, string replyFieldId, JsonElement notify)
    {
        if (notify.TryGetProperty("includeValues", out var include) && !include.GetBoolean())
        {
            return false;
        }

        return fields.EnumerateArray().Any(f =>
            f.GetProperty("id").GetString() == replyFieldId && f.GetProperty("type").GetString() == "email");
    }

    private static int? Int(JsonElement field, string name) =>
        field.TryGetProperty(name, out var value) ? value.GetInt32() : null;

    /// <summary>The schema's default <c>maxLength</c> per text-like type (form-block.md §2).</summary>
    private static int DefaultMaxLength(string type) => type switch
    {
        "textarea" => 10_000,
        "email" => 254,
        _ => 500,
    };
}

/// <summary>One broken publish rule: the rule and the field it concerns, if any.</summary>
internal sealed record FormSetupProblem(string Rule, string? FieldId);
