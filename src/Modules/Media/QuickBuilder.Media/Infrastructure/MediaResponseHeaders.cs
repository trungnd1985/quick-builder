using Microsoft.Net.Http.Headers;
using QuickBuilder.Media.Domain;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>How a file is being served, which decides its caching (013 contracts/public-media.md §1.3).</summary>
internal enum MediaResponseKind
{
    /// <summary>On the site's public surface: immutable, cached for a year.</summary>
    Public,

    /// <summary>An admin preview of the original: revalidated on every view (003 M-12).</summary>
    Preview,

    /// <summary>
    /// An admin variant, whose query names its content version: private, but immutable
    /// (013 MV-16).
    /// </summary>
    AdminVariant,

    /// <summary>
    /// The original sent at a variant's address because the variant could not be produced
    /// (013 MV-12): never cached, so nothing keeps the wrong bytes at that address.
    /// </summary>
    VariantFallback,
}

/// <summary>
/// The headers every served media file carries (003 FR-016, FR-017, research M-11, M-12;
/// 013 contracts/public-media.md §1.3).
/// </summary>
/// <remarks>
/// This is where SC-004 is decided: no uploaded file may ever be treated by a browser as a
/// page or script of the site.
/// <list type="bullet">
/// <item>The type is the one validated at upload, exactly, and <c>nosniff</c> forbids the
/// browser from guessing another — so bytes that also happen to be HTML are never HTML.</item>
/// <item>Images get a CSP <c>sandbox</c>: opened directly, they are a unique-origin document
/// that cannot touch the site's cookies or storage.</item>
/// <item>PDFs get no <c>sandbox</c>, because Chromium refuses to render PDFs in sandboxed
/// documents. Their viewers run any document script in their own isolated context. The
/// manual browser check in quickstart §4 is the merge gate for this (plan Risks).</item>
/// </list>
/// Public copies are immutable and cached for a year, because an address never starts
/// showing different content: a replacement gets a new address (013 MV-08). The
/// <c>ETag</c> names exactly what the address serves, version and width included, so an
/// admin preview, whose address does not change, never gets a 304 for replaced contents.
/// </remarks>
internal static class MediaResponseHeaders
{
    public const string ImageContentSecurityPolicy =
        "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";

    public const string PublicCacheControl = "public, max-age=31536000, immutable";

    public const string PreviewCacheControl = "private, no-cache";

    public const string AdminVariantCacheControl = "private, max-age=31536000, immutable";

    public const string NoStoreCacheControl = "no-store";

    /// <summary>
    /// Sets the safety and caching headers for <paramref name="address"/> of
    /// <paramref name="file"/>, and returns the validators to pass to <c>File(...)</c> so
    /// conditional and range requests are answered correctly.
    /// </summary>
    public static (EntityTagHeaderValue ETag, DateTimeOffset LastModified) Apply(
        HttpResponse response,
        MediaFile file,
        MediaAddress address,
        MediaResponseKind kind)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(file);

        var headers = response.Headers;
        headers.XContentTypeOptions = "nosniff";

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        headers.ContentDisposition = disposition.ToString();

        if (file.Type.IsImage)
        {
            headers.ContentSecurityPolicy = ImageContentSecurityPolicy;
        }

        headers.CacheControl = kind switch
        {
            MediaResponseKind.Public => PublicCacheControl,
            MediaResponseKind.AdminVariant => AdminVariantCacheControl,
            MediaResponseKind.VariantFallback => NoStoreCacheControl,
            _ => PreviewCacheControl,
        };

        // Strong: the bytes behind one version and width never change.
        return (new EntityTagHeaderValue($"\"{address.ETagValue}\""), file.ReplacedAtUtc ?? file.UploadedAtUtc);
    }
}
