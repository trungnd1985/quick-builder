using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Imaging;
using QuickBuilder.Pages.Contracts.Media;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Turns library rows into what the admin API returns (003 contracts/media-api.yaml, research
/// M-16, M-18; 013 contracts/media-api.yaml, research MV-05, MV-06, MV-16).
/// </summary>
/// <remarks>
/// Resolves uploader and replacer names in one call to Identity's directory, asks Pages once
/// for the page's usage, and works out <c>canDelete</c> and <c>canReplace</c> with the same
/// rules the handlers enforce (<see cref="MediaFileRules"/>). For listings it also checks that
/// each file's bytes are still there, so a file removed from storage by hand shows as
/// unavailable instead of failing the page (003 spec edge case).
/// </remarks>
internal sealed class MediaFileMapper(
    IUserDirectory users,
    IAccessEvaluator access,
    IFileStorage storage,
    IMediaUsageReader usage,
    IOptions<MediaOptions> options,
    ImagingRuntime imaging,
    ILogger<MediaFileMapper> logger)
{
    private const int AvailabilityChecksAtOnce = 8;

    public async Task<IReadOnlyList<MediaFileDto>> MapAsync(
        IReadOnlyList<MediaFile> files,
        Guid siteId,
        string pathBase,
        bool checkAvailability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return [];
        }

        var people = files.Select(f => f.UploadedBy)
            .Concat(files.Where(f => f.ReplacedBy is not null).Select(f => f.ReplacedBy!.Value))
            .Distinct()
            .ToList();
        var directory = (await users.FindManyAsync(people, cancellationToken).ConfigureAwait(false)).ToDictionary(u => u.Id);

        var usages = await usage.SummariseAsync(siteId, [.. files.Select(f => f.Id)], cancellationToken).ConfigureAwait(false);

        // Evaluated once per request, not per file: grants are read per request anyway.
        var rights = await MediaFileRules.RightsAsync(access, siteId, cancellationToken).ConfigureAwait(false);

        var missing = checkAvailability
            ? await FindMissingAsync(files, cancellationToken).ConfigureAwait(false)
            : [];

        if (missing.Count > 0)
        {
            logger.LogWarning(
                "Site {SiteId}: {Count} listed media file(s) have no stored bytes: {MediaFileIds}.",
                siteId, missing.Count, missing);
        }

        return
        [
            .. files.Select(f =>
            {
                var used = usages.GetValueOrDefault(f.Id) ?? MediaUsageSummary.None;
                var mayChange = rights.MayChange(f, used, access.UserId);
                return new MediaFileDto(
                    f.Id,
                    f.SiteId,
                    f.FileName,
                    f.ContentType,
                    f.SizeBytes,
                    f.UploadedAtUtc,
                    Person(directory, f.UploadedBy),
                    PublicPath(pathBase, f),
                    Available: !missing.Contains(f.Id),
                    CanDelete: mayChange,
                    f.FolderId,
                    f.AltText,
                    f.Width,
                    f.Height,
                    f.ContentVersion,
                    MetadataStateName(f.MetadataState),
                    f.ReplacedAtUtc,
                    f.ReplacedBy is { } replacer ? Person(directory, replacer) : null,
                    AdminVariants(pathBase, f),
                    ThumbnailPath(pathBase, f),
                    ToDto(used),
                    CanReplace: mayChange);
            }),
        ];
    }

    /// <summary>The file's public address under <paramref name="pathBase"/> (FR-013, research M-11, MV-08).</summary>
    public static string PublicPath(string pathBase, MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return pathBase.TrimEnd('/') + MediaAddress.Original(file).ToPath();
    }

    /// <summary>003's authenticated preview of the original, which also works for sites with no host name.</summary>
    public static string PreviewPath(string pathBase, MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return $"{pathBase.TrimEnd('/')}/api/v1/media/sites/{file.SiteId}/files/{file.Id}/content";
    }

    /// <summary>The usage summary as the API returns it.</summary>
    public static MediaUsageSummaryDto ToDto(MediaUsageSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new MediaUsageSummaryDto(summary.Places, summary.DraftPlaces, summary.LivePlaces, summary.HistoryOnlyPlaces, summary.InLiveUse);
    }

    /// <summary>The admin variant address; its query names the version, so it can be cached for good (MV-16).</summary>
    public static string AdminVariantPath(string pathBase, MediaFile file, int width)
    {
        ArgumentNullException.ThrowIfNull(file);
        return $"{pathBase.TrimEnd('/')}/api/v1/media/sites/{file.SiteId}/files/{file.Id}/variants/{width}?v={file.ContentVersion}";
    }

    /// <summary>Resized admin previews of <paramref name="file"/>, ascending (013 MV-16).</summary>
    private IReadOnlyList<MediaVariantDto> AdminVariants(string pathBase, MediaFile file) =>
    [
        .. VariantPlan.For(file, options.Value.Variants.Widths, imaging.AvifVariants)
            .Select(width => new MediaVariantDto(width, AdminVariantPath(pathBase, file, width))),
    ];

    /// <summary>
    /// What the admin shell shows as the file's thumbnail (013 FR-028): the thumbnail width when
    /// the file has it, otherwise 003's preview of the original.
    /// </summary>
    private string ThumbnailPath(string pathBase, MediaFile file)
    {
        var thumbnail = options.Value.Variants.ThumbnailWidth;
        return VariantPlan.Allows(file, thumbnail, options.Value.Variants.Widths, imaging.AvifVariants)
            ? AdminVariantPath(pathBase, file, thumbnail)
            : PreviewPath(pathBase, file);
    }

    private static string MetadataStateName(MediaMetadataState state) => state switch
    {
        MediaMetadataState.Pending => "pending",
        MediaMetadataState.Recorded => "recorded",
        MediaMetadataState.NotApplicable => "not_applicable",
        MediaMetadataState.OverLimit => "over_limit",
        MediaMetadataState.Unreadable => "unreadable",
        _ => ((short)state).ToString(CultureInfo.InvariantCulture),
    };

    private static UserSummaryDto Person(Dictionary<Guid, UserSummaryDto> directory, Guid id) =>
        directory.GetValueOrDefault(id) ?? new UserSummaryDto(id, string.Empty);

    private async Task<HashSet<Guid>> FindMissingAsync(IReadOnlyList<MediaFile> files, CancellationToken cancellationToken)
    {
        var missing = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = AvailabilityChecksAtOnce, CancellationToken = cancellationToken },
            async (file, token) =>
            {
                if (!await storage.ExistsAsync(new StorageKey(file.StorageKey), token).ConfigureAwait(false))
                {
                    missing.Add(file.Id);
                }
            }).ConfigureAwait(false);

        return [.. missing];
    }
}

/// <summary>
/// Who may delete or replace a file (003 research M-18, 013 research MV-06, MV-24): the one
/// rule the handlers enforce and the mapper reports.
/// </summary>
internal static class MediaFileRules
{
    /// <summary>What the caller holds on the site, looked up once.</summary>
    public static async Task<Rights> RightsAsync(IAccessEvaluator access, Guid siteId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var deleteAny = await access.HasCapabilityAsync(MediaCapabilities.Delete, siteId, cancellationToken).ConfigureAwait(false);
        var deleteOwn = await access.HasCapabilityAsync(MediaCapabilities.DeleteOwn, siteId, cancellationToken).ConfigureAwait(false);
        var publish = await access.HasCapabilityAsync(PublishingCapabilities.Manage, siteId, cancellationToken).ConfigureAwait(false);
        return new Rights(deleteAny, deleteOwn, publish);
    }

    /// <summary>The caller's capabilities that decide delete and replace.</summary>
    public sealed record Rights(bool DeleteAny, bool DeleteOwn, bool Publish)
    {
        /// <summary>
        /// Another person's file needs delete-any (003); a file visitors can see needs the
        /// publishing capability as well, whoever uploaded it (013 clarifications). The
        /// uploader is the original one, even after a replacement.
        /// </summary>
        public bool MayChange(MediaFile file, MediaUsageSummary usage, Guid? userId)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(usage);

            var ownership = DeleteAny || (DeleteOwn && userId is { } id && file.UploadedBy == id);
            return ownership && (!usage.InLiveUse || Publish);
        }
    }
}
