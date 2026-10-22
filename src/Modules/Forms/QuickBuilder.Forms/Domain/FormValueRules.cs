using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// What a visitor's values must be (018 FR-007, FR-015; research FS-08): normalised, then
/// checked against one field's rules, the first failure winning.
/// </summary>
/// <remarks>
/// <para>
/// The server is the authority; the admin's <c>formRules.ts</c> and the published form script
/// check the same rules first, and the fixture <c>tests/fixtures/forms/value-rules.json</c>
/// holds all three to the same answers.
/// </para>
/// <para>
/// Normalising: CR LF and CR become LF; single-line values are trimmed and their
/// line breaks become spaces; multi-line text loses trailing whitespace only; an empty value is
/// missing. Lengths are counted in code points, as the schema's <c>maxLength</c> is. There is
/// no Unicode normalisation: the product runs with invariant globalization, where
/// <c>string.Normalize</c> does nothing, and browsers send composed text.
/// </para>
/// </remarks>
internal static partial class FormValueRules
{
    /// <summary>The values to store for a field and why they fail, if they do.</summary>
    public static FieldCheck Check(FormField field, IReadOnlyList<string> submitted)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(submitted);

        return field.Type switch
        {
            FieldTypes.Checkboxes => CheckChoices(field, submitted),
            FieldTypes.Checkbox or FieldTypes.Consent => CheckTick(field, submitted),
            _ => CheckSingle(field, submitted),
        };
    }

    private static FieldCheck CheckSingle(FormField field, IReadOnlyList<string> submitted)
    {
        var value = submitted.Count == 0 ? string.Empty : Normalise(submitted[0], multiLine: field.Type == FieldTypes.Textarea);
        if (value.Trim().Length == 0)
        {
            return new FieldCheck(field.Required ? ValueCodes.Required : null, []);
        }

        return new FieldCheck(CodeOf(field, value), [value]);
    }

    private static string? CodeOf(FormField field, string value)
    {
        switch (field.Type)
        {
            case FieldTypes.Email:
                return value.Length > 254 || !Email().IsMatch(value) ? ValueCodes.Email : LengthCode(field, value);

            case FieldTypes.Phone:
                return !Phone().IsMatch(value) || !value.Any(char.IsAsciiDigit) ? ValueCodes.Phone : LengthCode(field, value);

            case FieldTypes.Number:
                if (!Number().IsMatch(value) || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    return ValueCodes.Number;
                }

                return field.WholeNumber && decimal.Truncate(number) != number ? ValueCodes.Whole
                    : number < field.Min ? ValueCodes.TooSmall
                    : number > field.Max ? ValueCodes.TooLarge
                    : null;

            case FieldTypes.Date:
                if (!IsoDate().IsMatch(value) || !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return ValueCodes.Date;
                }

                return date < field.Earliest ? ValueCodes.TooEarly
                    : date > field.Latest ? ValueCodes.TooLate
                    : null;

            case FieldTypes.Select or FieldTypes.Radio:
                return field.Options.Any(o => string.Equals(o.Value, value, StringComparison.Ordinal)) ? null : ValueCodes.Choice;

            default:
                return LengthCode(field, value);
        }
    }

    private static string? LengthCode(FormField field, string value)
    {
        var length = value.EnumerateRunes().Count();
        return length < field.MinLength ? ValueCodes.TooShort
            : length > (field.MaxLength ?? FieldTypes.DefaultMaxLength(field.Type)) ? ValueCodes.TooLong
            : null;
    }

    private static FieldCheck CheckChoices(FormField field, IReadOnlyList<string> submitted)
    {
        var chosen = submitted
            .Select(v => Normalise(v, multiLine: false))
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (chosen.Count == 0)
        {
            return new FieldCheck(field.Required ? ValueCodes.Required : null, []);
        }

        var values = field.Options.Select(o => o.Value).ToList();
        if (chosen.Any(c => !values.Contains(c, StringComparer.Ordinal)))
        {
            return new FieldCheck(ValueCodes.Choice, chosen);
        }

        // Stored in the order the options are, so the same choices always read the same.
        var ordered = values.Where(v => chosen.Contains(v, StringComparer.Ordinal)).ToList();
        var code = ordered.Count < field.MinSelected ? ValueCodes.TooFew
            : ordered.Count > field.MaxSelected ? ValueCodes.TooMany
            : null;
        return new FieldCheck(code, ordered);
    }

    private static FieldCheck CheckTick(FormField field, IReadOnlyList<string> submitted)
    {
        var ticked = submitted.Any(v => v.Trim().Length > 0);
        return ticked
            ? new FieldCheck(null, [FieldTypes.Ticked])
            : new FieldCheck(field.Required ? ValueCodes.Required : null, []);
    }

    /// <summary>LF line breaks, and the trimming the field's kind calls for.</summary>
    public static string Normalise(string value, bool multiLine)
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return multiLine ? text.TrimEnd() : text.Replace('\n', ' ').Trim();
    }

    // The page schema's own email shape (form-block.md §2, formNotify.recipients): one @, no
    // spaces or control characters, a dot in the domain. No DNS lookup: the product works offline.
    [GeneratedRegex(@"^[^\x00-\x20\x7f@]+@[^\x00-\x20\x7f@]+\.[^\x00-\x20\x7f@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"^[0-9 +()\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Number();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();
}

/// <summary>A field's values as stored, and the code of the rule they break (null when they pass).</summary>
internal sealed record FieldCheck(string? Code, IReadOnlyList<string> Values);

/// <summary>The value codes of public-forms.md §4.</summary>
internal static class ValueCodes
{
    public const string Required = "required";
    public const string TooShort = "too_short";
    public const string TooLong = "too_long";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string Number = "number";
    public const string Whole = "whole";
    public const string TooSmall = "too_small";
    public const string TooLarge = "too_large";
    public const string Date = "date";
    public const string TooEarly = "too_early";
    public const string TooLate = "too_late";
    public const string Choice = "choice";
    public const string TooFew = "too_few";
    public const string TooMany = "too_many";

    public static IReadOnlyList<string> All { get; } =
        [Required, TooShort, TooLong, Email, Phone, Number, Whole, TooSmall, TooLarge, Date, TooEarly, TooLate, Choice, TooFew, TooMany];
}
