using QuickBuilder.Identity.Contracts;

namespace QuickBuilder.Media.Contracts;

/// <summary>
/// One file in a site's media library, as the admin API returns it (003 contracts/media-api.yaml).
/// </summary>
/// <param name="Id">The file's id, also the first part of its public address.</param>
/// <param name="SiteId">The site whose library holds it.</param>
/// <param name="FileName">The original name, cleaned for display. Render as text, never as HTML.</param>
/// <param name="ContentType">The type detected from the file's contents.</param>
/// <param name="SizeBytes">Exact size in bytes.</param>
/// <param name="UploadedAtUtc">When it was stored and became public.</param>
/// <param name="UploadedBy">Who uploaded it.</param>
/// <param name="PublicPath">
/// The public path including any base path, e.g. <c>/_media/7d41….jpg</c>. The admin shell
/// prefixes it with a scheme and one of the site's host names; the server never guesses them.
/// </param>
/// <param name="Available">False when the bytes are missing from storage.</param>
/// <param name="CanDelete">Whether the caller may delete it. A courtesy for the UI; the server re-checks.</param>
public sealed record MediaFileDto(
    Guid Id,
    Guid SiteId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAtUtc,
    UserSummaryDto UploadedBy,
    string PublicPath,
    bool Available,
    bool CanDelete);

/// <summary>One page of a site's media library, newest first.</summary>
/// <param name="Items">The files on this page.</param>
/// <param name="TotalCount">How many files the site's library holds.</param>
/// <param name="NextCursor">Pass back to get the next page; null on the last page.</param>
public sealed record MediaFilePageDto(
    IReadOnlyList<MediaFileDto> Items,
    int TotalCount,
    string? NextCursor);
