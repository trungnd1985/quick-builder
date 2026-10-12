using QuickBuilder.Rendering.Binding;

namespace QuickBuilder.UnitTests.Rendering.Binding;

/// <summary>
/// How bound dates and numbers read, from the contract's formats file, with no locale data on
/// the server (016 research TB-09; binding-expression.md §4; tasks T040).
/// </summary>
public sealed class ValueFormatterTests
{
    [Theory]
    [InlineData("2026-10-10", null, "en", "Oct 10, 2026")]
    [InlineData("2026-10-10", "short", "en", "10/10/2026")]
    [InlineData("2026-10-10", "long", "en", "October 10, 2026")]
    [InlineData("2026-10-10", "iso", "en", "2026-10-10")]
    [InlineData("2026-03-04", "medium", "de", "4. März 2026")]
    [InlineData("2026-03-04", "short", "de", "04.03.2026")]
    [InlineData("2026-10-10", "long", "vi", "10 tháng 10, 2026")]
    [InlineData("2026-10-10T14:30:00Z", null, "en", "Oct 10, 2026 14:30 UTC")]
    [InlineData("2026-10-10T14:30:00Z", "iso", "en", "2026-10-10")]
    public void A_date_reads_in_the_languages_pattern(string value, string? style, string language, string expected) =>
        Assert.Equal(expected, ValueFormatter.Date(value, style, language));

    [Theory]
    [InlineData("10/10/2026")]
    [InlineData("2026-13-01")]
    [InlineData("2026-10-10T14:30:00")]
    public void Something_that_is_not_a_contract_date_is_not_formatted(string value) =>
        Assert.Null(ValueFormatter.Date(value, null, "en"));

    [Fact]
    public void A_language_the_formats_file_lacks_reads_as_english() =>
        Assert.Equal("Oct 10, 2026", ValueFormatter.Date("2026-10-10", null, "xx"));

    [Theory]
    [InlineData("1234", 0, "en", "1,234")]
    [InlineData("1234.5", 2, "en", "1,234.50")]
    [InlineData("1234.5", 0, "en", "1,235")]
    [InlineData("-2.5", 0, "en", "-3")]
    [InlineData("1234567.5", 2, "de", "1.234.567,50")]
    [InlineData("-0.004", 2, "en", "0.00")]
    [InlineData("999", 0, "en", "999")]
    public void A_number_is_rounded_half_away_from_zero_and_grouped_as_the_language_groups(string value, int decimals, string language, string expected) =>
        Assert.Equal(expected, ValueFormatter.Number(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), decimals, language));

    [Fact]
    public void A_publish_time_reads_as_its_date_in_utc() =>
        // 23:30 on the 5th at UTC−2 is 01:30 on the 6th in UTC.
        Assert.Equal("Jan 6, 2026", ValueFormatter.Date(new DateTimeOffset(2026, 1, 5, 23, 30, 0, TimeSpan.FromHours(-2)), null, "en"));
}
