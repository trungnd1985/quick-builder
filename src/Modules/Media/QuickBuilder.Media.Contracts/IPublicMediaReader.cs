namespace QuickBuilder.Media.Contracts;

/// <summary>
/// Where the public site finds media files (012 contracts/readers.md §1.3).
/// </summary>
/// <remarks>
/// Implemented by the Media module over a read-only connection (012 research PR-06). A file
/// that has been deleted is simply absent, and the renderer leaves its image out (FR-027).
/// </remarks>
public interface IPublicMediaReader
{
    /// <summary>The site's files among <paramref name="mediaIds"/>; ids that are not files of the site are absent.</summary>
    Task<IReadOnlyDictionary<Guid, PublicMediaFile>> GetAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default);
}

/// <summary>A media file as the public site links to it (013 contracts/readers.md §1.1).</summary>
/// <param name="PublicPath">
/// The address of the current content version's original, relative to the installation's
/// base path, e.g. <c>/_media/7d41….jpg</c> or <c>/_media/7d41…-v2.png</c>. The caller
/// prefixes the base path, as it does for page links.
/// </param>
/// <param name="ContentType">The type detected from the file's contents.</param>
/// <param name="Width">Its displayed width; null for documents and images not yet measured.</param>
/// <param name="Height">Its displayed height; null together with <paramref name="Width"/>.</param>
/// <param name="Variants">
/// Resized copies, ascending by width, each strictly narrower than the original. Empty for
/// documents, GIFs, animated images and files whose variants are unavailable (013 MV-11).
/// </param>
/// <param name="OfferOriginal">
/// Whether a page's <c>srcset</c> may also offer the original at its own width: only when it is
/// no wider than the largest configured variant width, so a huge original is never downloaded
/// (013 contracts/public-media.md §2, SC-003).
/// </param>
public sealed record PublicMediaFile(
    string PublicPath,
    string ContentType,
    int? Width,
    int? Height,
    IReadOnlyList<PublicMediaVariant> Variants,
    bool OfferOriginal = false);

/// <summary>One resized copy of a file (013 research MV-15).</summary>
/// <param name="Width">Its width in pixels, one of the configured widths.</param>
/// <param name="PublicPath">Its address relative to the installation's base path.</param>
public sealed record PublicMediaVariant(int Width, string PublicPath);
