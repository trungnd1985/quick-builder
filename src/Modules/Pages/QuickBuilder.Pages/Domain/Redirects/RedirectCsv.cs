using System.Text;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Domain.Redirects;

/// <summary>
/// The redirect import and export file: RFC 4180 comma-separated values in UTF-8, with the
/// columns <c>source,target,kind,match,note</c> (017 FR-019, research SE-13).
/// </summary>
/// <remarks>
/// About eighty lines instead of a dependency (Principle XI). Reading accepts a byte-order mark,
/// CRLF, LF or CR line ends, an optional header row and blank lines, and refuses a file that is
/// not valid UTF-8 as a whole. Writing quotes what needs quoting and puts a <c>'</c> before a
/// cell a spreadsheet would run as a formula.
/// </remarks>
internal static class RedirectCsv
{
    public static readonly string[] Header = ["source", "target", "kind", "match", "note"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The file's rows, each with the line it starts on, header and blank lines left out.</summary>
    public static Result<IReadOnlyList<RedirectCsvLine>> Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Result.Failure<IReadOnlyList<RedirectCsvLine>>(PagesErrors.RedirectImportEncoding);
        }

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var lines = new List<RedirectCsvLine>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var startLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    if (c == '\n' || (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
                    {
                        line++;
                    }

                    cell.Append(c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? string.Empty : c.ToString());
                }
            }
            else if (c == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRow();
                line++;
                startLine = line;
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            EndRow();
        }

        return Result.Success<IReadOnlyList<RedirectCsvLine>>(lines);

        void EndRow()
        {
            cells.Add(cell.ToString());
            cell.Clear();
            var row = cells.ToArray();
            cells.Clear();

            var blank = row.Length == 1 && row[0].Trim().Length == 0;
            var header = lines.Count == 0 && string.Equals(row[0].Trim(), Header[0], StringComparison.OrdinalIgnoreCase);
            if (!blank && !header)
            {
                lines.Add(new RedirectCsvLine(startLine, row));
            }
        }
    }

    /// <summary>The status code a <c>kind</c> cell names, or null when it names none.</summary>
    public static int? StatusCodeOf(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "301" or "permanent" => 301,
        "302" or "temporary" => 302,
        _ => null,
    };

    /// <summary>A file with the header row and one row per redirect, CRLF line ends.</summary>
    public static string Write(IEnumerable<RedirectCsvRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRow(builder, Header);
        foreach (var row in rows)
        {
            AppendRow(builder, [row.Source, row.Target, row.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Match, row.Note ?? string.Empty]);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var cell = cells[i];
            if (cell.Length > 0 && cell[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                cell = "'" + cell;
            }

            builder.Append(cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell);
        }

        builder.Append("\r\n");
    }
}

/// <summary>One row read from a file, with the line it starts on (1-based).</summary>
internal sealed record RedirectCsvLine(int Line, string[] Cells);

/// <summary>One redirect as the export writes it.</summary>
internal sealed record RedirectCsvRow(string Source, string Target, int StatusCode, string Match, string? Note);
