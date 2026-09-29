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

/// <summary>A media file as the public site links to it.</summary>
/// <param name="PublicPath">
/// Its address relative to the installation's base path, e.g. <c>/_media/7d41….jpg</c>. The
/// caller prefixes the base path, as it does for page links.
/// </param>
/// <param name="ContentType">The type detected from the file's contents.</param>
public sealed record PublicMediaFile(string PublicPath, string ContentType);
