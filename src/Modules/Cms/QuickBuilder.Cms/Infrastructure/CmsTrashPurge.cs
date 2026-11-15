using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Deletes a trashed item permanently (021 FR-022, research TR-13, TR-14): as 015 FR-008 deletes
/// an item, with its versions, schedules, history and derived rows through their foreign keys.
/// </summary>
/// <remarks>
/// The row is deleted only while it is still in the trash (and due, for the retention job), so a
/// restore that won keeps it and each entry is purged once (FR-020). The item's history goes with
/// it (spec FR-022), so the purge is recorded in the log, by id only (FR-028).
/// </remarks>
internal sealed class CmsTrashPurge(CmsDbContext database, ILogger<CmsTrashPurge> logger)
{
    private const string Reason = "Deleting a trashed item permanently (021 TR-14).";

    /// <summary>Purges one item; <paramref name="actor"/> is null for the retention job.</summary>
    public async Task<Result> PurgeAsync(Guid siteId, Guid itemId, Guid? actor, DateTimeOffset? dueBefore, CancellationToken cancellationToken)
    {
        var deleted = await database.ForSite<Item>(siteId).OnlyTrash(Reason)
            .Where(i => i.Id == itemId && (dueBefore == null || i.TrashedAtUtc <= dueBefore))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deleted == 0)
        {
            return Result.Failure(CmsErrors.NotInTrash);
        }

        logger.LogInformation(
            "Trashed item {ItemId} of site {SiteId} deleted permanently by {Actor}.",
            itemId, siteId, actor is { } who ? who.ToString() : "the system");
        return Result.Success();
    }
}
