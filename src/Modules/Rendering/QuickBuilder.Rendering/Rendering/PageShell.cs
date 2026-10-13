using System.Security.Cryptography;
using System.Text;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Seo;

namespace QuickBuilder.Rendering.Rendering;

/// <summary>
/// The document around a page's blocks (012 research PR-12, public-rendering.md §3).
/// </summary>
/// <remarks>
/// The body is wrapped in <c>qb-canvas qb-page</c>, the same reset and the same named
/// container the editor's canvas frame uses, so the one block stylesheet lays blocks out the
/// same way in both (page-schema.md §9). No script is ever written.
/// </remarks>
internal static class PageShell
{
    /// <summary>A complete document, and the hash its content security policy allows.</summary>
    /// <param name="Html">The document.</param>
    /// <param name="StyleHash">Base64 SHA-256 of the one style element's text, for <c>style-src</c>.</param>
    internal sealed record Document(string Html, string StyleHash);

    public static Document Write(string title, string language, string basePath, TokenCss tokens, string nodeRules, string body, HeadMetadata? head = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var style = new StringBuilder(".qb-page{");
        foreach (var (name, value) in tokens.Properties())
        {
            style.Append(name).Append(':').Append(value).Append(';');
        }

        style.Append('}').Append(nodeRules);

        var css = style.ToString();

        // Nothing a design or a document holds can end the style element (TokenCss and the
        // renderers only write variables, enums and checked values); this is the backstop.
        if (css.Contains('<', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A style value would break out of the style element.");
        }

        var writer = new HtmlWriter()
            .Markup("<!doctype html>\n")
            .Open("html").Attribute("lang", language).EndTag().Markup("\n")
            .Markup("<head>\n")
            .Markup("<meta charset=\"utf-8\">\n")
            .Markup("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");

        // 017 research SE-05: a published page says what it is. The not-found page and the
        // golden renders, which have no site, keep only their title.
        if (head is null)
        {
            writer.Markup("<title>").Text(title).Markup("</title>\n");
        }
        else
        {
            HeadWriter.Write(writer, head);
        }

        writer.Open("link").Attribute("rel", "stylesheet");
        writer.SitePath("href", $"{TrimBase(basePath)}/_assets/{BlockStylesheet.Instance.FileName}");
        writer.EndTag().Markup("\n")
            .Markup("<style>").Markup(css).Markup("</style>\n")
            .Markup("</head>\n")
            .Markup("<body class=\"qb-published\">\n")
            .Markup("<div class=\"qb-canvas qb-page\">").Markup(body).Markup("</div>\n")
            .Markup("</body>\n")
            .Markup("</html>\n");

        return new Document(writer.ToString(), Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(css))));
    }

    /// <summary>The base path without a trailing slash; empty at the root.</summary>
    public static string TrimBase(string? basePath) => (basePath ?? string.Empty).TrimEnd('/');
}
