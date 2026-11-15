using QuickBuilder.Identity.Contracts;

namespace QuickBuilder.Media.Contracts;

/// <summary>
/// One file in a site's media library, as the admin API returns it (003 contracts/media-api.yaml,
/// extended by 013 contracts/media-api.yaml).
/// </summary>
/// <param name="Id">The file's id, also the first part of its public address.</param>
/// <param name="SiteId">The site whose library holds it.</param>
/// <param name="FileName">The original name, cleaned for display. Render as text, never as HTML.</param>
/// <param name="ContentType">The type detected from the current version's contents.</param>
/// <param name="SizeBytes">Exact size in bytes.</param>
/// <param name="UploadedAtUtc">When it was first stored and became public.</param>
/// <param name="UploadedBy">Who first uploaded it. Never changes, even when replaced.</param>
/// <param name="PublicPath">
/// The public path of the current version including any base path, e.g. <c>/_media/7d41….jpg</c>.
/// The admin shell prefixes it with a scheme and one of the site's host names; the server never
/// guesses them.
/// </param>
/// <param name="Available">False when the bytes are missing from storage.</param>
/// <param name="CanDelete">Whether the caller may delete it. A courtesy for the UI; the server re-checks.</param>
/// <param name="FolderId">The folder it is filed in; null at the top level (013).</param>
/// <param name="AltText">Alternative text; images only, null when none (013). Render as text.</param>
/// <param name="Width">Displayed width, or null (documents, or not measured yet).</param>
/// <param name="Height">Displayed height, or null.</param>
/// <param name="ContentVersion">1 until the first replacement (013).</param>
/// <param name="MetadataState"><c>pending</c>, <c>recorded</c>, <c>not_applicable</c>, <c>over_limit</c> or <c>unreadable</c>.</param>
/// <param name="ReplacedAtUtc">When the current contents replaced earlier ones; null if never.</param>
/// <param name="ReplacedBy">Who replaced them; null if never.</param>
/// <param name="Variants">Admin addresses of resized previews, ascending; empty when not eligible (013 MV-16).</param>
/// <param name="ThumbnailPath">Where the admin shell loads its thumbnail from, including any base path.</param>
/// <param name="Usage">How many pages and components use it (013 FR-007).</param>
/// <param name="CanReplace">Whether the caller may replace its contents. A courtesy; the server re-checks.</param>
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
    bool CanDelete,
    Guid? FolderId,
    string? AltText,
    int? Width,
    int? Height,
    int ContentVersion,
    string MetadataState,
    DateTimeOffset? ReplacedAtUtc,
    UserSummaryDto? ReplacedBy,
    IReadOnlyList<MediaVariantDto> Variants,
    string ThumbnailPath,
    MediaUsageSummaryDto Usage,
    bool CanReplace);

/// <summary>One resized preview of a file, for the admin shell.</summary>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Path">Its admin address, including any base path.</param>
public sealed record MediaVariantDto(int Width, string Path);

/// <summary>How many places use a file (013 contracts/media-api.yaml <c>UsageSummary</c>).</summary>
public sealed record MediaUsageSummaryDto(int Places, int DraftPlaces, int LivePlaces, int HistoryOnlyPlaces, bool InLiveUse, int TrashedPlaces = 0)
{
    public static MediaUsageSummaryDto None { get; } = new(0, 0, 0, 0, false);
}

/// <summary>One page of a site's media library.</summary>
/// <param name="Items">The files on this page.</param>
/// <param name="TotalCount">How many files match the query (the whole library when there is none).</param>
/// <param name="NextCursor">Pass back to get the next page; null on the last page.</param>
public sealed record MediaFilePageDto(
    IReadOnlyList<MediaFileDto> Items,
    int TotalCount,
    string? NextCursor);
