using Microsoft.EntityFrameworkCore;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Domain;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Answers Pages and Cms before a draft save: may this session write? (023 research CE-07,
/// module-surface.md §2). One indexed read of the live holder.
/// </summary>
internal sealed class EditingLocks(EditorDbContext database, TimeProvider clock) : IEditingLocks
{
    public async Task<EditingWriteCheck> CheckWriteAsync(
        Guid siteId,
        string documentKind,
        Guid documentId,
        Guid? sessionId,
        CancellationToken cancellationToken = default)
    {
        // No session: an API client, or an admin from before 023. The version check decides (FR-026).
        if (sessionId is null)
        {
            return EditingWriteCheck.Yes;
        }

        var cutoff = clock.GetUtcNow() - EditingRules.Expiry;
        var holder = await database.ForSite<EditingSession>(siteId)
            .AsNoTracking()
            .Where(s => s.DocumentKind == documentKind && s.DocumentId == documentId && s.HoldsLock && s.LastSeenAt > cutoff)
            .Select(s => new { s.Id, s.UserId, s.LockTakenAt })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return holder is null || holder.Id == sessionId
            ? EditingWriteCheck.Yes
            : EditingWriteCheck.HeldBy(holder.UserId, holder.LockTakenAt ?? DateTimeOffset.MinValue);
    }

    public async Task<EditingHolder?> FindActiveHolderAsync(
        Guid siteId,
        string documentKind,
        Guid documentId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Live (seen within the expiry) and active (not idle past the stale period): a stale or
        // gone holder does not stop a delete (021 clarification Q2).
        var now = clock.GetUtcNow();
        var seenAfter = now - EditingRules.Expiry;
        var activeAfter = now - EditingRules.StalePeriod;
        var holder = await database.ForSite<EditingSession>(siteId)
            .AsNoTracking()
            .Where(s => s.DocumentKind == documentKind && s.DocumentId == documentId && s.HoldsLock
                        && s.LastSeenAt > seenAfter && s.LastActivityAt > activeAfter)
            .Select(s => new { s.UserId, s.LockTakenAt })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return holder is null || holder.UserId == userId
            ? null
            : new EditingHolder(holder.UserId, holder.LockTakenAt ?? now);
    }
}
