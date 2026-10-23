using System.Text;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// Writes CSV that spreadsheet applications open correctly and cannot be tricked by (018 FR-029,
/// SC-007; research FS-16).
/// </summary>
/// <remarks>
/// RFC 4180: fields with a comma, a quote or a line break are quoted, quotes doubled, rows end
/// with CR LF. The file starts with a UTF-8 byte order mark, so spreadsheet applications read
/// non-Latin text right. A value starting with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab or
/// carriage return is prefixed with <c>'</c>, so it is shown as text and never run as a formula
/// (OWASP, CSV injection).
/// </remarks>
internal sealed class CsvWriter(TextWriter output)
{
    /// <summary>UTF-8 with its byte order mark, for the response stream.</summary>
    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public async Task WriteRowAsync(IEnumerable<string> fields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var line = new StringBuilder();
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                line.Append(',');
            }

            first = false;
            line.Append(Field(field));
        }

        line.Append("\r\n");
        await output.WriteAsync(line.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One field, made safe and quoted when it must be.</summary>
    public static string Field(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return text.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }
}
