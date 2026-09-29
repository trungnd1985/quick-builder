using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// The only way a renderer writes HTML (012 research PR-09, FR-025).
/// </summary>
/// <remarks>
/// Content never reaches the output unencoded: text goes through <see cref="Text"/>, attribute
/// values through <see cref="Attribute"/>, and addresses through <see cref="Url"/> or
/// <see cref="SitePath"/>, which check them first. Tag and attribute names are the renderers'
/// own constants. <see cref="Markup"/> exists for those constants and is not for content;
/// review any call that passes it something built from a document.
/// </remarks>
internal sealed partial class HtmlWriter
{
    // Every script is encoded, except that letters outside ASCII stay readable; < > & " ' are
    // always encoded, which is what makes text and quoted attribute values safe.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private readonly StringBuilder _output = new(capacity: 16 * 1024);

    /// <summary>Writes constant markup. Never content (see remarks).</summary>
    public HtmlWriter Markup(string markup)
    {
        _output.Append(markup);
        return this;
    }

    /// <summary>Writes text content, HTML-encoded.</summary>
    /// <remarks>
    /// Line breaks are written as they are, not as <c>&amp;#xA;</c>: a Text block keeps its
    /// lines through <c>white-space: pre-wrap</c>, and the markup then reads the same as the
    /// canvas's. A newline can end nothing in HTML, so leaving it unencoded is safe.
    /// </remarks>
    public HtmlWriter Text(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return this;
        }

        using var sink = new StringWriter(_output, System.Globalization.CultureInfo.InvariantCulture);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                _output.Append('\n');
            }

            Encoder.Encode(sink, lines[i]);
        }

        return this;
    }

    /// <summary>Opens <c>&lt;tag</c>; follow with attributes, then <see cref="EndTag"/>.</summary>
    public HtmlWriter Open(string tag) => Markup("<").Markup(tag);

    /// <summary>Closes the opening tag: <c>&gt;</c>.</summary>
    public HtmlWriter EndTag() => Markup(">");

    /// <summary>Writes <c>&lt;/tag&gt;</c>.</summary>
    public HtmlWriter Close(string tag) => Markup("</").Markup(tag).Markup(">");

    /// <summary>Writes <c> name="value"</c> with the value attribute-encoded; nothing when it is null.</summary>
    public HtmlWriter Attribute(string name, string? value)
    {
        if (value is null)
        {
            return this;
        }

        Markup(" ").Markup(name).Markup("=\"");
        Text(value);
        return Markup("\"");
    }

    /// <summary>
    /// Writes an external address attribute, only when its scheme is one the page schema allows
    /// (https, http, mailto, tel). Returns false, writing nothing, for anything else.
    /// </summary>
    public bool Url(string name, string? url)
    {
        if (url is null || url.Length > 2048 || !AllowedExternalUrl().IsMatch(url))
        {
            return false;
        }

        Attribute(name, url);
        return true;
    }

    /// <summary>
    /// Writes an address on this installation: it must start with one <c>/</c>, so it can never
    /// leave the site or carry a scheme.
    /// </summary>
    public bool SitePath(string name, string path)
    {
        if (!IsSitePath(path))
        {
            return false;
        }

        Attribute(name, path);
        return true;
    }

    /// <summary>Whether <paramref name="path"/> is a root-relative address on this installation.</summary>
    public static bool IsSitePath(string? path) =>
        path is { Length: > 0 } && path[0] == '/' && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal);

    /// <summary>The HTML written so far.</summary>
    public override string ToString() => _output.ToString();

    // The page schema's own pattern for url links (docs/contracts/page-schema.md §2a).
    [GeneratedRegex("^(https?://[^\\u0000-\\u0020\\u007f]+|mailto:[^\\u0000-\\u0020\\u007f]+|tel:[+0-9 ().-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedExternalUrl();
}
