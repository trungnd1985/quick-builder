using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QuickBuilder.Cms.Contracts;

namespace QuickBuilder.Cms.Domain;

/// <summary>What a media value points at, as far as a conversion needs to know (research CS-12).</summary>
internal enum MediaKind
{
    Image,
    Document,

    /// <summary>The file no longer exists; the value is kept (spec edge case).</summary>
    Missing,
}

/// <summary>A value after a conversion: what happened to it, and the value to store (null for none).</summary>
internal readonly record struct Converted(ValueOutcome Outcome, JsonNode? Value);

/// <summary>
/// The value conversions of contract §7 (research CS-07): one function per allowed type change
/// and per setting change that rewrites values. Pure and culture-invariant; dates are UTC.
/// </summary>
/// <remarks>
/// Whether the result still meets the new field's settings is measured afterwards, by the
/// caller, so a converted value that breaks them is kept and counted as needing attention.
/// </remarks>
internal static partial class FieldConversion
{
    private const int MaxRichTextBlocks = 500;
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly string[] DateTimeInputs =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    /// <summary>
    /// Converts one present value from <paramref name="before"/>'s definition to
    /// <paramref name="after"/>'s: the type change if there is one, otherwise the setting
    /// changes that rewrite values, and the options that no longer exist.
    /// </summary>
    public static Converted Convert(Field before, Field after, JsonNode value, Func<Guid, MediaKind> mediaKind)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(mediaKind);

        if (before.Type != after.Type)
        {
            return ConvertType(before, after, value, mediaKind);
        }

        var result = new Converted(ValueOutcome.Kept, value);

        switch (after.Type)
        {
            case FieldType.Option when after.FindOption(value.GetValue<string>()) is null && before.FindOption(value.GetValue<string>()) is not null:
                return new Converted(ValueOutcome.Cleared, null);
            case FieldType.Text when before.Multiline && !after.Multiline:
                var joined = JoinLines(value.GetValue<string>());
                result = joined == value.GetValue<string>() ? result : new Converted(ValueOutcome.Converted, JsonValue.Create(joined));
                break;
            case FieldType.Number when after.Decimals < before.Decimals:
                var number = value.GetValue<decimal>();
                var rounded = Round(number, after.Decimals);
                result = rounded == number ? result : new Converted(ValueOutcome.PartlyLost, JsonValue.Create(rounded));
                break;
            case FieldType.Date when !before.WithTime && after.WithTime:
                result = new Converted(ValueOutcome.Converted, JsonValue.Create(value.GetValue<string>() + "T00:00:00Z"));
                break;
            case FieldType.Date when before.WithTime && !after.WithTime:
                var moment = ParseStoredDateTime(value.GetValue<string>());
                var date = moment.ToString(DateFormat, CultureInfo.InvariantCulture);
                result = new Converted(moment.TimeOfDay == TimeSpan.Zero ? ValueOutcome.Converted : ValueOutcome.PartlyLost, JsonValue.Create(date));
                break;
        }

        return result;
    }

    private static Converted ConvertType(Field before, Field after, JsonNode value, Func<Guid, MediaKind> mediaKind) =>
        (before.Type, after.Type) switch
        {
            (FieldType.Text, FieldType.RichText) => new(ValueOutcome.Converted, TextToRichText(value.GetValue<string>())),
            (FieldType.RichText, FieldType.Text) => RichTextToText(value, after),
            (FieldType.Number, FieldType.Text) => new(ValueOutcome.Converted, JsonValue.Create(NumberToText(value.GetValue<decimal>()))),
            (FieldType.Text, FieldType.Number) => TextToNumber(value.GetValue<string>(), after),
            (FieldType.Date, FieldType.Text) => new(ValueOutcome.Converted, JsonValue.Create(value.GetValue<string>())),
            (FieldType.Text, FieldType.Date) => TextToDate(value.GetValue<string>(), after.WithTime),
            (FieldType.Option, FieldType.Text) => before.FindOption(value.GetValue<string>()) is { } option
                ? new(ValueOutcome.Converted, JsonValue.Create(option.Label))
                : new(ValueOutcome.Cleared, null),
            (FieldType.Text, FieldType.Option) => after.Options.FirstOrDefault(o => Fold.Comparer.Equals(o.Label, value.GetValue<string>().Trim())) is { } match
                ? new(ValueOutcome.Converted, JsonValue.Create(match.Id))
                : new(ValueOutcome.Cleared, null),
            (FieldType.Boolean, FieldType.Text) => new(ValueOutcome.Converted, JsonValue.Create(value.GetValue<bool>() ? "true" : "false")),
            (FieldType.Image, FieldType.File) => new(ValueOutcome.Kept, value),
            (FieldType.File, FieldType.Image) => mediaKind(MediaIdOf(value)) switch
            {
                MediaKind.Image => new(ValueOutcome.Kept, value),
                MediaKind.Missing => new(ValueOutcome.Missing, value),
                _ => new(ValueOutcome.Cleared, null),
            },
            (FieldType.Reference, FieldType.MultiReference) => new(ValueOutcome.Converted, new JsonArray(JsonValue.Create(value.GetValue<string>()))),
            (FieldType.MultiReference, FieldType.Reference) => value.AsArray() is { Count: > 0 } items
                ? new(items.Count == 1 ? ValueOutcome.Converted : ValueOutcome.PartlyLost, JsonValue.Create(items[0]!.GetValue<string>()))
                : new(ValueOutcome.Cleared, null),
            _ => throw new InvalidOperationException($"No conversion from {before.Type} to {after.Type}; the change is forbidden."),
        };

    public static Guid MediaIdOf(JsonNode value) => Guid.Parse(value["mediaId"]!.GetValue<string>(), CultureInfo.InvariantCulture);

    public static decimal Round(decimal number, int decimals) => decimal.Round(number, decimals, MidpointRounding.AwayFromZero);

    /// <summary>Invariant, shortest form: no grouping, <c>.</c> as the separator, no trailing zeros.</summary>
    public static string NumberToText(decimal number) =>
        number.ToString("0.############################", CultureInfo.InvariantCulture);

    private static Converted TextToNumber(string text, Field after)
    {
        var trimmed = text.Trim();
        if (!NumberText().IsMatch(trimmed)
            || !decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return new(ValueOutcome.Cleared, null);
        }

        var rounded = Round(number, after.Decimals);
        if (Math.Abs(rounded) > CollectionSchemaLimits.MaxSafeInteger
            || (after.Min is { } min && rounded < min)
            || (after.Max is { } max && rounded > max))
        {
            return new(ValueOutcome.Cleared, null);
        }

        return new(rounded == number ? ValueOutcome.Converted : ValueOutcome.PartlyLost, JsonValue.Create(rounded));
    }

    private static Converted TextToDate(string text, bool withTime)
    {
        var trimmed = text.Trim();
        var isDate = DateOnly.TryParseExact(trimmed, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);

        if (!withTime)
        {
            return isDate ? new(ValueOutcome.Converted, JsonValue.Create(date.ToString(DateFormat, CultureInfo.InvariantCulture))) : new(ValueOutcome.Cleared, null);
        }

        if (isDate)
        {
            return new(ValueOutcome.Converted, JsonValue.Create(date.ToString(DateFormat, CultureInfo.InvariantCulture) + "T00:00:00Z"));
        }

        if (!DateTimeOffset.TryParseExact(trimmed, DateTimeInputs, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return new(ValueOutcome.Cleared, null);
        }

        var utc = moment.ToUniversalTime();
        var seconds = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, TimeSpan.Zero);
        return new(
            seconds == utc ? ValueOutcome.Converted : ValueOutcome.PartlyLost,
            JsonValue.Create(seconds.ToString(DateTimeFormat, CultureInfo.InvariantCulture)));
    }

    private static DateTimeOffset ParseStoredDateTime(string text) =>
        DateTimeOffset.ParseExact(text, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static string JoinLines(string text) => LineBreak().Replace(text, " ");

    private static string[] Lines(string text) => LineBreak().Split(text);

    /// <summary>
    /// Each line becomes a paragraph. Past the page schema's 500 blocks, consecutive lines
    /// share paragraphs evenly, separated by hard breaks, so nothing is lost.
    /// </summary>
    public static JsonObject TextToRichText(string text)
    {
        var lines = Lines(text);
        var perParagraph = (int)Math.Ceiling(lines.Length / (double)MaxRichTextBlocks);
        var blocks = new JsonArray();

        for (var start = 0; start < lines.Length; start += perParagraph)
        {
            var inline = new JsonArray();
            for (var i = start; i < Math.Min(start + perParagraph, lines.Length); i++)
            {
                if (i > start)
                {
                    inline.Add(new JsonObject { ["type"] = "hardBreak" });
                }

                if (lines[i].Length > 0)
                {
                    inline.Add(new JsonObject { ["type"] = "text", ["text"] = lines[i] });
                }
            }

            var paragraph = new JsonObject { ["type"] = "paragraph" };
            if (inline.Count > 0)
            {
                paragraph["content"] = inline;
            }

            blocks.Add(paragraph);
        }

        return new JsonObject { ["type"] = "doc", ["content"] = blocks };
    }

    private static Converted RichTextToText(JsonNode document, Field after)
    {
        var separator = after.Multiline ? "\n" : " ";
        var lost = false;
        var blocks = new List<string>();

        void Block(JsonNode block)
        {
            switch (block["type"]?.GetValue<string>())
            {
                case "paragraph":
                    var line = new StringBuilder();
                    foreach (var inline in block["content"]?.AsArray() ?? [])
                    {
                        if (inline!["type"]!.GetValue<string>() == "hardBreak")
                        {
                            line.Append(separator);
                        }
                        else
                        {
                            line.Append(inline["text"]!.GetValue<string>());
                            lost |= inline["marks"] is JsonArray { Count: > 0 };
                        }
                    }

                    blocks.Add(line.ToString());
                    break;
                default:
                    // A list: its items' blocks become lines, and the list itself is dropped.
                    lost = true;
                    foreach (var item in block["content"]?.AsArray() ?? [])
                    {
                        foreach (var child in item!["content"]?.AsArray() ?? [])
                        {
                            Block(child!);
                        }
                    }

                    break;
            }
        }

        foreach (var block in document["content"]?.AsArray() ?? [])
        {
            Block(block!);
        }

        var text = string.Join(separator, blocks);
        if (text.Length == 0)
        {
            return new(ValueOutcome.Cleared, null);
        }

        var runes = text.EnumerateRunes().ToList();
        if (runes.Count > after.MaxLength)
        {
            text = string.Concat(runes.Take(after.MaxLength).Select(r => r.ToString()));
            lost = true;
        }

        return new(lost ? ValueOutcome.PartlyLost : ValueOutcome.Converted, JsonValue.Create(text));
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberText();

    [GeneratedRegex(@"\r\n|\r|\n", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreak();
}
