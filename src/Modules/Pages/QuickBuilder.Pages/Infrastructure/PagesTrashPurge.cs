using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Deletes a trashed page or component permanently (021 FR-022, research TR-13, TR-14): what a
/// delete removed before 021 — its draft, versions, publication, schedules and usage rows — with
/// a <c>purged</c> entry in the publish log, which keeps its earlier entries (012 FR-006).
/// </summary>
/// <remarks>
/// Used by the Administrators' permanent delete and empty, and by the retention job. Each entry
/// is its own transaction. The row is deleted only while it is still in the trash: a restore
/// that committed first leaves nothing to delete, and one waiting on the row sees it gone, so
/// exactly one of them wins (FR-020).
/// </remarks>
internal sealed class PagesTrashPurge(PagesDbContext database, PublishingWrites publishing, ILogger<PagesTrashPurge> logger)
{
    private const string Reason = "Deleting a trashed page or component permanently (021 TR-14).";

    /// <summary>Purges one entry; <paramref name="actor"/> is null for the retention job.</summary>
    public async Task<Result> PurgeAsync(Guid siteId, string kind, Guid id, Guid? actor, DateTimeOffset? dueBefore, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        PublishingWrites.Subject? subject;
        int deleted;
        if (kind == SubjectKinds.Page)
        {
            var page = await database.ForSite<Page>(siteId).OnlyTrash(Reason).AsNoTracking()
                .Where(p => p.Id == id && (dueBefore == null || p.TrashedAtUtc <= dueBefore))
                .Select(p => new { p.Id, p.Title, p.TrashedPath })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                return Result.Failure(PagesErrors.NotInTrash);
            }

            subject = new PublishingWrites.Subject(SubjectKinds.Page, page.Id, siteId, page.Title, page.TrashedPath);
            deleted = await database.ForSite<Page>(siteId).OnlyTrash(Reason)
                .Where(p => p.Id == id)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var component = await database.ForSite<Component>(siteId).OnlyTrash(Reason).AsNoTracking()
                .Where(c => c.Id == id && (dueBefore == null || c.TrashedAtUtc <= dueBefore))
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (component is null)
            {
                return Result.Failure(PagesErrors.NotInTrash);
            }

            subject = new PublishingWrites.Subject(SubjectKinds.Component, component.Id, siteId, component.Name, Path: null);
            deleted = await database.ForSite<Component>(siteId).OnlyTrash(Reason)
                .Where(c => c.Id == id)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        // A restore committed between the read and the delete.
        if (deleted == 0)
        {
            return Result.Failure(PagesErrors.NotInTrash);
        }

        await publishing.ForgetAsync(subject, actor, purge: true, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Ids only (FR-028): a title or an address can itself be content.
        logger.LogInformation(
            "Trashed {SubjectKind} {SubjectId} of site {SiteId} deleted permanently (by the system: {BySystem}).",
            kind, id, siteId, actor is null);
        return Result.Success();
    }
}
