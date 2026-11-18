using System.Security.Cryptography;
using System.Text;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.RenderPage;

namespace QuickBuilder.Rendering.Features.Preview;

/// <summary>
/// Every response under <c>/_preview</c> (022 research PV-10, public-preview.md §5): never
/// stored, never indexed, never passing its address on.
/// </summary>
/// <remarks>
/// The one place these headers are set; <c>PreviewBoundaryTests</c> require every preview
/// action to answer through it. No <c>ETag</c> or <c>304</c>: a preview is rendered fresh on
/// every request.
/// </remarks>
internal static class PreviewResponse
{
    public const string HtmlType = "text/html; charset=utf-8";

    /// <summary>The headers every preview response carries, whatever else it carries.</summary>
    public static Dictionary<string, string> BaseHeaders() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cache-Control"] = "no-store, private",
        ["X-Robots-Tag"] = "noindex, nofollow",
        ["Referrer-Policy"] = "no-referrer",
        ["X-Content-Type-Options"] = "nosniff",
        ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()",
    };

    /// <summary>A rendered preview page, with the policy its content allows (012 PR-12).</summary>
    public static PublicResponse Page(RenderedPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var headers = BaseHeaders();
        var policy = PageResponder.ContentSecurityPolicy(page.StyleHash, page.ScriptHash, page.FormScriptHash, page.FormActionOrigins);

        // PV-12: a preview's forms post, without a script, to /_preview/form on the same origin.
        headers["Content-Security-Policy"] = page.PostsToPreview
            ? policy.Replace("form-action 'none'", "form-action 'self'", StringComparison.Ordinal)
            : policy;
        return new PublicResponse(StatusCodes.Status200OK, HtmlType, headers, page.Html);
    }

    /// <summary>A fixed page: one sentence, its own style element, nothing else (PV-10).</summary>
    public static PublicResponse Fixed(int status, string title, string message, TimeSpan? retryAfter = null)
    {
        const string Style = "body{font:16px/1.5 system-ui,sans-serif;margin:0;padding:3rem 1rem;color:#1f2933;background:#fff}main{max-width:36rem;margin:0 auto}h1{font-size:1.5rem;margin:0 0 .75rem}";

        var html = new StringBuilder()
            .Append("<!doctype html><html lang=\"").Append(PreviewTexts.Language).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta name=\"robots\" content=\"noindex, nofollow\"><meta name=\"referrer\" content=\"no-referrer\">")
            .Append("<title>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>")
            .Append("<style>").Append(Style).Append("</style></head><body><main>")
            .Append("<h1>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</h1>")
            .Append("<p>").Append(System.Net.WebUtility.HtmlEncode(message)).Append("</p>")
            .Append("</main></body></html>")
            .ToString();

        var headers = BaseHeaders();
        headers["Content-Security-Policy"] =
            $"default-src 'none'; style-src 'sha256-{Hash(Style)}'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        if (retryAfter is { } wait)
        {
            headers["Retry-After"] = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new PublicResponse(status, HtmlType, headers, Encoding.UTF8.GetBytes(html));
    }

    /// <summary>
    /// A media file as a preview serves it: the file's own type, length and validators, with
    /// the preview headers instead of the public caching ones (public-preview.md §5).
    /// </summary>
    public static Dictionary<string, string> MediaHeaders(string etag, DateTimeOffset lastModifiedUtc)
    {
        var headers = BaseHeaders();
        headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        headers["ETag"] = etag;
        headers["Last-Modified"] = lastModifiedUtc.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return headers;
    }

    private static string Hash(string style) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(style)));
}
