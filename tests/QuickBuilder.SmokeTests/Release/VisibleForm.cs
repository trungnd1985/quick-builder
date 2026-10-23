using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickBuilder.SmokeTests.Release;

/// <summary>
/// What a visitor reads on a page and where its links lead, without the markup around it
/// (019 contracts/snapshot.md §3, research RL-17).
/// </summary>
/// <remarks>
/// Between releases the markup, class names and stylesheet may change — 012 renders every
/// page again after such an upgrade — but what visitors read and where links lead may not.
/// <para>
/// A hand-written tokenizer, not an HTML parser: it only ever reads the server's own
/// well-formed output, and no parser package is referenced anywhere in the solution.
/// </para>
/// <para>Pure and Docker-free; the unit tests compile this file in.</para>
/// </remarks>
internal static partial class VisibleForm
{
    private static readonly string[] Attributes = ["href", "src", "srcset", "action", "alt"];

    /// <summary>The visible form: a <c>title:</c> line, a <c>text:</c> line, then one line per attribute.</summary>
    public static string Of(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var title = new StringBuilder();
        var text = new StringBuilder();
        var attributes = new List<string>();
        var inBody = false;
        var inTitle = false;
        var i = 0;

        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                var end = html.IndexOf('<', i);
                end = end < 0 ? html.Length : end;
                var chunk = html[i..end];
                if (inTitle)
                {
                    title.Append(chunk);
                }
                else if (inBody)
                {
                    text.Append(chunk).Append(' ');
                }

                i = end;
                continue;
            }

            if (html.AsSpan(i).StartsWith("<!--"))
            {
                var close = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = close < 0 ? html.Length : close + 3;
                continue;
            }

            var tagEnd = FindTagEnd(html, i + 1);
            var tag = html[(i + 1)..tagEnd];
            i = Math.Min(tagEnd + 1, html.Length);

            if (tag.StartsWith('!') || tag.StartsWith('?'))
            {
                continue;
            }

            var closing = tag.StartsWith('/');
            var name = TagName(closing ? tag[1..] : tag);

            if (closing)
            {
                inTitle &= name != "title";
                inBody &= name != "body";

                // An inline element closing joins words; a block closing separates them.
                text.Append(' ');
                continue;
            }

            if (name is "script" or "style")
            {
                // Raw text: skip to the matching end tag; nothing inside is visible.
                var close = html.IndexOf($"</{name}", i, StringComparison.OrdinalIgnoreCase);
                i = close < 0 ? html.Length : close;
            }

            inTitle |= name == "title";
            inBody |= name == "body";

            foreach (var (attribute, value) in ReadAttributes(tag))
            {
                if (Attributes.Contains(attribute))
                {
                    attributes.Add($"{attribute}: {Normalise(WebUtility.HtmlDecode(value))}");
                }
            }

            text.Append(' ');
        }

        var lines = new List<string>
        {
            $"title: {Collapse(WebUtility.HtmlDecode(title.ToString()))}",
            $"text: {Collapse(WebUtility.HtmlDecode(text.ToString()))}",
        };
        lines.AddRange(attributes);
        return string.Join('\n', lines);
    }

    /// <summary><c>sha256:</c> and the hex hash of <see cref="Of"/>.</summary>
    public static string Fingerprint(string html) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Of(html))));

    /// <summary>The <c>text:</c> line alone, for diffs people can read.</summary>
    public static string Text(string html) =>
        Of(html).Split('\n')[1]["text: ".Length..];

    private static int FindTagEnd(string html, int from)
    {
        char? quote = null;
        for (var j = from; j < html.Length; j++)
        {
            var c = html[j];
            if (quote is { } q)
            {
                if (c == q)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return j;
            }
        }

        return html.Length;
    }

    private static string TagName(string tag)
    {
        var end = 0;
        while (end < tag.Length && !char.IsWhiteSpace(tag[end]) && tag[end] is not ('/' or '>'))
        {
            end++;
        }

        return tag[..end].ToLowerInvariant();
    }

    private static IEnumerable<(string Name, string Value)> ReadAttributes(string tag)
    {
        foreach (Match match in Attribute().Matches(tag))
        {
            var value = match.Groups["dq"].Success ? match.Groups["dq"].Value
                : match.Groups["sq"].Success ? match.Groups["sq"].Value
                : match.Groups["uq"].Value;
            yield return (match.Groups["name"].Value.ToLowerInvariant(), value);
        }
    }

    /// <summary>Strips the content hash Rendering puts in asset file names (blocks.3f2a.css → blocks.css).</summary>
    private static string Normalise(string value) => HashedAsset().Replace(value, "${name}.${ext}");

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<uq>[^\s""'=<>`]+))", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"(?<name>[\w-]+)\.[0-9a-f]{8,}\.(?<ext>css|js)\b", RegexOptions.CultureInvariant)]
    private static partial Regex HashedAsset();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
