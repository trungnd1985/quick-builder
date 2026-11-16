using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Deletes a trashed file permanently (021 FR-022, research TR-13, TR-14): the row first, then its
/// bytes, as the delete of 003 did. Variants and bytes that cannot be removed now are left to the
/// orphan sweep (003 FR-012), which no longer sees a row naming them.
/// </summary>
/// <remarks>
/// The row is deleted only while it is still in the trash (and due, for the retention job), so a
/// restore that won keeps it and each file is purged once (FR-020).
/// </remarks>
internal sealed class MediaTrashPurge(MediaDbContext database, IFileStorage storage, ILogger<MediaTrashPurge> logger)
{
    private const string Reason = "Deleting a trashed file permanently (021 TR-14).";

    /// <summary>Purges one file; <paramref name="actor"/> is null for the retention job.</summary>
    public async Task<Result> PurgeAsync(Guid siteId, Guid fileId, Guid? actor, DateTimeOffset? dueBefore, CancellationToken cancellationToken)
    {
        var files = database.MediaFiles
            .AcrossAllSites("A purge of a file on a site named explicitly (001 FR-011b).")
            .OnlyTrash(Reason)
            .Where(f => f.SiteId == siteId && f.Id == fileId && (dueBefore == null || f.TrashedAtUtc <= dueBefore));

        var key = await files.Select(f => f.StorageKey).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (key is null || await files.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            return Result.Failure(MediaErrors.NotInTrash);
        }

        logger.LogInformation(
            "Trashed media file {MediaFileId} of site {SiteId} deleted permanently by {Actor}.",
            fileId, siteId, actor is { } who ? who.ToString() : "the system");

        try
        {
            await storage.DeleteAsync(new StorageKey(key), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex,
                "The bytes of purged media file {MediaFileId} on site {SiteId} could not be removed; the orphan sweep will remove them.",
                fileId, siteId);
        }

        return Result.Success();
    }
}
