using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Shared.Infrastructure.Caching;

namespace QuickBuilder.Rendering.Features.RenderPage;

/// <summary>
/// Everything a rendered page depends on, as numbers read before its content (012 research
/// PR-05, data-model §5).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The page's <c>generation</c> moves on with every change to the page, to what it shows
/// and to what it links to, in the transaction of that change.</item>
/// <item>The design number moves on with every design publish.</item>
/// <item>The site's version moves on with its settings, such as its language.</item>
/// <item>The renderer stamp moves on with the stylesheet and the build.</item>
/// <item>The page numbers its paginated lists show, normalised by <see cref="RenderPage.PageParams"/> (016 TB-11).</item>
/// </list>
/// So a change gives the page a new key on every instance at its next request, and no cache
/// entry is ever wrong for its key: the key is read before the content, so the content
/// cached under it is at least as new as the key.
/// </remarks>
internal sealed record RenderKey(
    Guid SiteId,
    Guid PageId,
    long Generation,
    int DesignNumber,
    uint SiteVersion,
    string BasePath,
    string PageParams = "")
{
    /// <summary>The stylesheet's hash and the build, which change the output of every page.</summary>
    public static string RendererStamp { get; } =
        BlockStylesheet.Instance.Hash + "-"
        + (typeof(RenderKey).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(RenderKey).Assembly.GetName().Version?.ToString()
           ?? "0");

    /// <summary>The key's string form, scoped to the site (constitution Principle V).</summary>
    public string CacheKey =>
        Shared.Infrastructure.Caching.CacheKey.ForSite(
            SiteId, "page", $"{PageId:N}:{Generation}:{DesignNumber}:{SiteVersion}:{BasePath}:{PageParams}:{RendererStamp}");

    /// <summary>A strong validator: the same key always renders the same bytes.</summary>
    public string ETag => ETagOf(CacheKey);

    /// <summary>Whether an <c>If-None-Match</c> header names this key's ETag.</summary>
    public bool IsMatchedBy(string? ifNoneMatch) => Matches(ETag, ifNoneMatch);

    /// <summary>The validator of a cache key (also used by item pages, 016).</summary>
    public static string ETagOf(string cacheKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey));
        return "\"" + Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "\"";
    }

    /// <summary>Whether an <c>If-None-Match</c> header names <paramref name="etag"/>.</summary>
    public static bool Matches(string etag, string? ifNoneMatch)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        foreach (var candidate in ifNoneMatch.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // Weak comparison, as RFC 9110 §13.1.2 asks of If-None-Match.
            var value = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
            if (value == "*" || string.Equals(value, etag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
