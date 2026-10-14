using System.Text;
using QuickBuilder.Pages.Domain.Redirects;

namespace QuickBuilder.UnitTests.Pages.Redirects;

/// <summary>The import and export file (017 FR-019, research SE-13).</summary>
public sealed class RedirectCsvTests
{
    [Fact]
    public void Rows_are_read_with_their_line_numbers()
    {
        var rows = Read("/a,/b,301\n/c,https://elsewhere.test/,302,exact,Old link\n");

        Assert.Equal([1, 2], rows.Select(r => r.Line));
        Assert.Equal(["/c", "https://elsewhere.test/", "302", "exact", "Old link"], rows[1].Cells);
    }

    [Fact]
    public void A_header_row_is_recognised_and_skipped()
    {
        var rows = Read("source,target,kind,match,note\n/a,/b,301\n");

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Line);
    }

    [Fact]
    public void Quoted_cells_hold_commas_quotes_and_line_breaks()
    {
        var rows = Read("\"/a,b\",/c,301,exact,\"He said \"\"hi\"\"\nthen left\"\n/d,/e,301\n");

        Assert.Equal("/a,b", rows[0].Cells[0]);
        Assert.Equal("He said \"hi\"\nthen left", rows[0].Cells[4]);
        Assert.Equal(3, rows[1].Line);
    }

    [Fact]
    public void A_byte_order_mark_and_windows_line_ends_are_accepted()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("/a,/b,301\r\n/c,/d,302\r\n")).ToArray();

        var read = RedirectCsv.Read(bytes);

        Assert.True(read.IsSuccess);
        Assert.Equal(["/c", "/d", "302"], read.Value[1].Cells);
    }

    [Fact]
    public void Blank_lines_are_skipped()
    {
        Assert.Equal(2, Read("/a,/b,301\n\n/c,/d,302\n\n").Count);
    }

    [Fact]
    public void A_file_that_is_not_utf8_is_refused_as_a_whole()
    {
        var latin1 = new byte[] { (byte)'/', 0xE9, (byte)',', (byte)'/', (byte)'b', (byte)',', (byte)'3', (byte)'0', (byte)'1' };

        var read = RedirectCsv.Read(latin1);

        Assert.True(read.IsFailure);
        Assert.Equal("redirects.import_encoding", read.Error.Code);
    }

    [Theory]
    [InlineData("301", 301)]
    [InlineData("302", 302)]
    [InlineData("permanent", 301)]
    [InlineData("Temporary", 302)]
    [InlineData(" 301 ", 301)]
    public void Kinds_are_read_in_every_spelling(string cell, int status)
    {
        Assert.Equal(status, RedirectCsv.StatusCodeOf(cell));
    }

    [Theory]
    [InlineData("307")]
    [InlineData("")]
    [InlineData("moved")]
    public void Other_kinds_are_not_understood(string cell)
    {
        Assert.Null(RedirectCsv.StatusCodeOf(cell));
    }

    [Fact]
    public void The_export_writes_a_header_and_quotes_what_needs_it()
    {
        var csv = RedirectCsv.Write([
            new RedirectCsvRow("/a,b", "/c", 301, "exact", null),
            new RedirectCsvRow("/d", "https://elsewhere.test/", 302, "prefix", "A \"quoted\" note"),
        ]);

        Assert.Equal(
            "source,target,kind,match,note\r\n\"/a,b\",/c,301,exact,\r\n/d,https://elsewhere.test/,302,prefix,\"A \"\"quoted\"\" note\"\r\n",
            csv);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tx")]
    [InlineData("\rx")]
    public void Cells_a_spreadsheet_would_run_as_formulas_get_a_leading_quote(string note)
    {
        var csv = RedirectCsv.Write([new RedirectCsvRow("/a", "/b", 301, "exact", note)]);

        Assert.Contains("'" + note[0], csv, StringComparison.Ordinal);
    }

    [Fact]
    public void An_export_reads_back_to_the_same_cells()
    {
        var written = RedirectCsv.Write([new RedirectCsvRow("/a", "/b", 301, "exact", "Note, with comma")]);

        var rows = RedirectCsv.Read(Encoding.UTF8.GetBytes(written)).Value;

        Assert.Equal(["/a", "/b", "301", "exact", "Note, with comma"], rows.Single().Cells);
    }

    private static IReadOnlyList<RedirectCsvLine> Read(string text)
    {
        var read = RedirectCsv.Read(Encoding.UTF8.GetBytes(text));
        Assert.True(read.IsSuccess);
        return read.Value;
    }
}
