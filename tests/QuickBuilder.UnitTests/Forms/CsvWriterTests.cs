using QuickBuilder.Forms.Infrastructure;

namespace QuickBuilder.UnitTests.Forms;

/// <summary>CSV that spreadsheets read right and cannot be tricked by (018 FR-029, SC-007; research FS-16).</summary>
public sealed class CsvWriterTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("Nguyễn 😀", "Nguyễn 😀")]
    public void Fields_are_quoted_only_when_they_must_be(string value, string expected) =>
        Assert.Equal(expected, CsvWriter.Field(value));

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1 555", "'+1 555")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tlead", "'\tlead")]
    [InlineData("\rlead", "\"'\rlead\"")]
    [InlineData("a=b", "a=b")]
    public void Values_that_spreadsheets_would_run_are_written_as_text(string value, string expected) =>
        Assert.Equal(expected, CsvWriter.Field(value));

    [Fact]
    public async Task Rows_end_with_cr_lf_and_the_file_starts_with_a_byte_order_mark()
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, CsvWriter.Encoding, leaveOpen: true))
        {
            var csv = new CsvWriter(writer);
            await csv.WriteRowAsync(["a", "b"], CancellationToken.None);
            await csv.WriteRowAsync(["c", "d,e"], CancellationToken.None);
        }

        var bytes = stream.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("a,b\r\nc,\"d,e\"\r\n", System.Text.Encoding.UTF8.GetString(bytes[3..]));
    }
}
