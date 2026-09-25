using Microsoft.Net.Http.Headers;
using QuickBuilder.Media.Domain;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>Whether a file is being served to the public or previewed in the admin shell.</summary>
internal enum MediaResponseKind
{
    Public,
    Preview,
}

/// <summary>
/// The headers every served media file carries (003 FR-016, FR-017, research M-11, M-12).
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
/// showing different content (FR-005). Previews revalidate every time, so every view is
/// re-authorized (002 FR-017) while an unchanged file costs only a 304.
/// </remarks>
internal static class MediaResponseHeaders
{
    public const string ImageContentSecurityPolicy =
        "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";

    public const string PublicCacheControl = "public, max-age=31536000, immutable";

    public const string PreviewCacheControl = "private, no-cache";

    /// <summary>
    /// Sets the safety and caching headers, and returns the validators to pass to
    /// <c>File(...)</c> so conditional and range requests are answered correctly.
    /// </summary>
    public static (EntityTagHeaderValue ETag, DateTimeOffset LastModified) Apply(
        HttpResponse response,
        MediaFile file,
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

        headers.CacheControl = kind == MediaResponseKind.Public ? PublicCacheControl : PreviewCacheControl;

        // Strong: the bytes behind an id never change.
        return (new EntityTagHeaderValue($"\"{file.Id:N}\""), file.UploadedAtUtc);
    }
}
