using Microsoft.Extensions.Logging;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Turns library rows into what the admin API returns (003 contracts/media-api.yaml, research M-16, M-18).
/// </summary>
/// <remarks>
/// Resolves uploader names in one call to Identity's directory, works out <c>canDelete</c>
/// with the same rule the delete handler enforces, and — for listings — checks that each
/// file's bytes are still there, so a file removed from disk by hand shows as unavailable
/// instead of failing the page (spec edge case).
/// </remarks>
internal sealed class MediaFileMapper(
    IUserDirectory users,
    IAccessEvaluator access,
    IFileStorage storage,
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

        var uploaders = (await users
                .FindManyAsync([.. files.Select(f => f.UploadedBy).Distinct()], cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(u => u.Id);

        // Evaluated once per request, not per file: grants are read per request anyway.
        var canDeleteAny = await access.HasCapabilityAsync(MediaCapabilities.Delete, siteId, cancellationToken).ConfigureAwait(false);
        var canDeleteOwn = await access.HasCapabilityAsync(MediaCapabilities.DeleteOwn, siteId, cancellationToken).ConfigureAwait(false);

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
            .. files.Select(f => new MediaFileDto(
                f.Id,
                f.SiteId,
                f.FileName,
                f.ContentType,
                f.SizeBytes,
                f.UploadedAtUtc,
                uploaders.GetValueOrDefault(f.UploadedBy) ?? new UserSummaryDto(f.UploadedBy, string.Empty),
                PublicPath(pathBase, f),
                Available: !missing.Contains(f.Id),
                CanDelete: canDeleteAny || (canDeleteOwn && f.UploadedBy == access.UserId))),
        ];
    }

    /// <summary>The file's public address under <paramref name="pathBase"/> (FR-013, research M-11).</summary>
    public static string PublicPath(string pathBase, MediaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return $"{pathBase.TrimEnd('/')}/_media/{file.Id:N}.{file.Extension}";
    }

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
