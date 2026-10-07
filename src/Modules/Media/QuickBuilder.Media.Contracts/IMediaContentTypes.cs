namespace QuickBuilder.Media.Contracts;

/// <summary>
/// The content types of a site's media files, for administrative work in other modules
/// (014 research CS-12).
/// </summary>
/// <remarks>
/// The Cms module asks it to tell images from documents when a schema change converts or
/// re-checks media values. Unlike <see cref="IPublicMediaReader"/>, which serves the public
/// site and resolves its site from the request's host name, this answers for the site the
/// caller names, as the admin surface does. A file's kind never changes (013 FR-017).
/// </remarks>
public interface IMediaContentTypes
{
    /// <summary>The content type of each of the site's files among <paramref name="mediaIds"/>; others are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetAsync(
        Guid siteId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken = default);
}
