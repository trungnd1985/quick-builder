using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace QuickBuilder.Rendering.Binding;

/// <summary>
/// How a bound date or number reads in the site's language (016 research TB-09;
/// binding-expression.md §4).
/// </summary>
/// <remarks>
/// The product runs with invariant globalization, so it has no locale data of its own. The
/// patterns, month names and separators come from the contract's
/// <c>binding-expression/formats.json</c>, embedded here and imported by the editor, so the
/// canvas and the published page read the same. A language the file lacks reads as English.
/// </remarks>
internal static class ValueFormatter
{
    public const string FallbackLanguage = "en";

    private const string Resource = "QuickBuilder.Rendering.BindingFormats.json";

    private static readonly Lazy<JsonDocument> Formats = new(() =>
    {
        using var stream = typeof(ValueFormatter).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("binding-expression/formats.json is not embedded in the Rendering module.");
        return JsonDocument.Parse(stream);
    });

    private static readonly ConcurrentDictionary<string, LanguageFormats> Loaded = new(StringComparer.Ordinal);

    /// <summary>A date value (<c>YYYY-MM-DD</c>, or <c>YYYY-MM-DDTHH:MM:SSZ</c>) in a style, or null when it is not one.</summary>
    public static string? Date(string value, string? style, string language)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Date(date, null, style, language);
        }

        if (DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var moment))
        {
            return Date(DateOnly.FromDateTime(moment), TimeOnly.FromDateTime(moment), style, language);
        }

        return null;
    }

    /// <summary>A moment as a date in a style: the built-in publish times, which are dates to readers.</summary>
    public static string Date(DateTimeOffset moment, string? style, string language) =>
        Date(DateOnly.FromDateTime(moment.UtcDateTime), null, style, language);

    /// <summary>
    /// A date, and a time when the value has one, in a style: <c>short</c>, <c>medium</c> (also
    /// when none is given), <c>long</c> or <c>iso</c>. A time reads as <c>HH:mm UTC</c>, except
    /// in <c>iso</c>, which is the date alone.
    /// </summary>
    public static string Date(DateOnly date, TimeOnly? time, string? style, string language)
    {
        if (style == "iso")
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var formats = For(language);
        var pattern = style switch
        {
            "short" => formats.Short,
            "long" => formats.Long,
            _ => formats.Medium,
        };

        var text = Apply(pattern, date, formats);
        return time is { } t ? $"{text} {t.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC" : text;
    }

    /// <summary>A number rounded to <paramref name="decimals"/> places, grouped by thousands as the language groups them.</summary>
    public static string Number(decimal value, int decimals, string language)
    {
        var formats = For(language);
        var rounded = Math.Round(value, Math.Clamp(decimals, 0, 6), MidpointRounding.AwayFromZero);
        var invariant = rounded.ToString("F" + Math.Clamp(decimals, 0, 6).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        var negative = invariant.StartsWith('-');
        var digits = negative ? invariant[1..] : invariant;
        var point = digits.IndexOf('.', StringComparison.Ordinal);
        var whole = point < 0 ? digits : digits[..point];
        var fraction = point < 0 ? string.Empty : digits[(point + 1)..];

        var grouped = new StringBuilder();
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0)
            {
                grouped.Append(formats.Group);
            }

            grouped.Append(whole[i]);
        }

        var result = fraction.Length > 0 ? $"{grouped}{formats.Decimal}{fraction}" : grouped.ToString();
        return negative && rounded != 0 ? "-" + result : result;
    }

    private static string Apply(string pattern, DateOnly date, LanguageFormats formats)
    {
        var output = new StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            if (pattern[i] == '{' && pattern.IndexOf('}', i) is var end and > 0)
            {
                output.Append(pattern[(i + 1)..end] switch
                {
                    "d" => date.Day.ToString(CultureInfo.InvariantCulture),
                    "dd" => date.Day.ToString("00", CultureInfo.InvariantCulture),
                    "M" => date.Month.ToString(CultureInfo.InvariantCulture),
                    "MM" => date.Month.ToString("00", CultureInfo.InvariantCulture),
                    "MMM" => formats.MonthsShort[date.Month - 1],
                    "MMMM" => formats.MonthsLong[date.Month - 1],
                    "yyyy" => date.Year.ToString("0000", CultureInfo.InvariantCulture),
                    _ => string.Empty,
                });
                i = end + 1;
            }
            else
            {
                output.Append(pattern[i]);
                i++;
            }
        }

        return output.ToString();
    }

    private static LanguageFormats For(string language) =>
        Loaded.GetOrAdd(language, tag => Read(tag) ?? Read(FallbackLanguage)
            ?? throw new InvalidOperationException("binding-expression/formats.json has no English entry."));

    private static LanguageFormats? Read(string language)
    {
        if (!Formats.Value.RootElement.TryGetProperty(language, out var entry))
        {
            return null;
        }

        static string[] Names(JsonElement e, string name) => [.. e.GetProperty(name).EnumerateArray().Select(m => m.GetString()!)];

        return new LanguageFormats(
            entry.GetProperty("short").GetString()!,
            entry.GetProperty("medium").GetString()!,
            entry.GetProperty("long").GetString()!,
            Names(entry, "monthsShort"),
            Names(entry, "monthsLong"),
            entry.GetProperty("decimal").GetString()!,
            entry.GetProperty("group").GetString()!);
    }

    private sealed record LanguageFormats(
        string Short, string Medium, string Long, string[] MonthsShort, string[] MonthsLong, string Decimal, string Group);
}
