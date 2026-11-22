namespace QuickBuilder.Media.Contracts;

/// <summary>
/// A current media file of the site in the request's site context, by its public address
/// (022 research PV-11, module-surface.md §2.5).
/// </summary>
/// <remarks>
/// For previews, which are served under <c>/_preview</c> on any host name and so cannot use
/// <c>/_media</c>, which answers only on the site's own host names. The same parsing, version,
/// variant and trash rules apply as on <c>/_media</c> (013 contracts/public-media.md §1): an
/// address that would not be served there is not served here.
/// </remarks>
public interface IMediaContent
{
    /// <summary>
    /// The file at <paramref name="address"/>, the part after <c>/_media/</c> (for example
    /// <c>7d41….jpg</c>); null when <c>/_media</c> would answer not found. The caller disposes it.
    /// </summary>
    Task<MediaFileContent?> OpenAsync(string address, CancellationToken cancellationToken = default);
}

/// <summary>A media file's bytes and what a response needs to describe them.</summary>
/// <param name="FileId">The media file the address belongs to, for the caller's own access rule.</param>
/// <param name="FileName">The file's name, for <c>Content-Disposition</c>. Content: never log it.</param>
/// <param name="ETag">A strong entity tag, quoted, for the bytes behind this address.</param>
/// <param name="IsImage">Whether the file is an image, which gets the image content security policy.</param>
public sealed record MediaFileContent(
    Guid FileId,
    Stream Content,
    string ContentType,
    string FileName,
    string ETag,
    DateTimeOffset LastModifiedUtc,
    bool IsImage) : IDisposable, IAsyncDisposable
{
    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
