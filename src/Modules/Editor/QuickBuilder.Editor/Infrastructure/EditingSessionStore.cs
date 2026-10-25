using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Features.EditingSessions;
using QuickBuilder.Identity.Contracts;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Reads and writes editing sessions (023 data-model §1.1, research CE-02 to CE-06).
/// </summary>
/// <remarks>
/// <para>
/// Writes are single statements whose <c>WHERE</c> states the rule they apply, so two
/// instances racing for one document get one answer each: the partial unique index on
/// <c>holds_lock</c> lets one claim win (FR-010), and a takeover or move names the holder it
/// expects, so it changes nothing if that holder changed meanwhile. Tracked entities would
/// write back what they read, and could undo a takeover that committed in between.
/// </para>
/// <para>
/// "Gone" is never stored: every read and every claim compares <c>last_seen_at</c> with the
/// server's clock (<see cref="EditingRules.Expiry"/>), so a crashed browser's lock lapses on
/// time whether or not the cleanup service has run.
/// </para>
/// </remarks>
internal sealed class EditingSessionStore(EditorDbContext database, IUserDirectory users)
{
    private const string HolderIndex = "ux_editing_sessions_holder";

    /// <summary>The caller's own live session, or null: unknown, someone else's, or gone.</summary>
    public Task<EditingSession?> OwnAsync(Guid siteId, Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - EditingRules.Expiry;
        return database.ForSite<EditingSession>(siteId)
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.LastSeenAt > cutoff, cancellationToken);
    }

    /// <summary>Every live session on one document.</summary>
    public Task<List<EditingSession>> LiveAsync(Guid siteId, string kind, Guid documentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - EditingRules.Expiry;
        return database.ForSite<EditingSession>(siteId)
            .AsNoTracking()
            .Where(s => s.DocumentKind == kind && s.DocumentId == documentId && s.LastSeenAt > cutoff)
            .OrderBy(s => s.OpenedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The live holder of one document, or null.</summary>
    public Task<EditingSession?> HolderAsync(Guid siteId, string kind, Guid documentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - EditingRules.Expiry;
        return database.ForSite<EditingSession>(siteId)
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.DocumentKind == kind && s.DocumentId == documentId && s.HoldsLock && s.LastSeenAt > cutoff, cancellationToken);
    }

    /// <summary>The live holders of one kind on a site, for list markers (FR-012).</summary>
    public Task<List<EditingSession>> HoldersAsync(Guid siteId, string kind, Guid? collectionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - EditingRules.Expiry;
        var query = database.ForSite<EditingSession>(siteId)
            .AsNoTracking()
            .Where(s => s.DocumentKind == kind && s.HoldsLock && s.LastSeenAt > cutoff);
        if (collectionId is { } collection)
        {
            query = query.Where(s => s.CollectionId == collection);
        }

        return query.OrderBy(s => s.LockTakenAt).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(EditingSession session, CancellationToken cancellationToken)
    {
        database.EditingSessions.Add(session);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A heartbeat. Activity moves only for the holder (spec edge case: reading is not activity).</summary>
    public async Task TouchAsync(EditingSession session, bool active, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var own = database.ForSite<EditingSession>(session.SiteId).Where(s => s.Id == session.Id);
        await own.ExecuteUpdateAsync(s => s.SetProperty(e => e.LastSeenAt, now), cancellationToken).ConfigureAwait(false);
        if (active)
        {
            await own.Where(s => s.HoldsLock).ExecuteUpdateAsync(s => s.SetProperty(e => e.LastActivityAt, now), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes the lock for <paramref name="session"/>: clears a gone holder, then sets
    /// <c>holds_lock</c>; the unique index refuses it when a live session holds the lock. True
    /// when the session holds the lock afterwards.
    /// </summary>
    public async Task<bool> ClaimAsync(EditingSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.HoldsLock)
        {
            return true;
        }

        var owned = database.Database.CurrentTransaction is null;
        await using var transaction = owned ? await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        try
        {
            var cutoff = now - EditingRules.Expiry;
            await database.ForSite<EditingSession>(session.SiteId)
                .Where(s => s.DocumentKind == session.DocumentKind && s.DocumentId == session.DocumentId && s.HoldsLock && s.LastSeenAt <= cutoff)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await TakeAsync(session.SiteId, session.Id, now, cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (IsHolderConflict(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return false;
        }
    }

    /// <summary>
    /// Gives the lock of <paramref name="holderSessionId"/> to <paramref name="session"/> in one
    /// transaction, but only if the holder still holds it, is still alive and, when
    /// <paramref name="staleBefore"/> is set, has been inactive since before it. The former holder
    /// remembers who took it and why. False when nothing moved.
    /// </summary>
    public async Task<bool> TransferAsync(
        EditingSession session,
        Guid holderSessionId,
        string reason,
        DateTimeOffset? staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Joins the caller's transaction when there is one (a takeover also writes its record).
        var owned = database.Database.CurrentTransaction is null;
        await using var transaction = owned ? await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        var cutoff = now - EditingRules.Expiry;
        var holder = database.ForSite<EditingSession>(session.SiteId).Where(s =>
            s.Id == holderSessionId
            && s.DocumentKind == session.DocumentKind
            && s.DocumentId == session.DocumentId
            && s.HoldsLock
            && s.LastSeenAt > cutoff);
        if (staleBefore is { } before)
        {
            holder = holder.Where(s => s.LastActivityAt <= before);
        }

        var released = await holder.ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.HoldsLock, false)
                    .SetProperty(e => e.LockTakenAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.LastActivityAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.LostLockAt, now)
                    .SetProperty(e => e.LostLockToUserId, session.UserId)
                    .SetProperty(e => e.LostLockReason, reason),
                cancellationToken)
            .ConfigureAwait(false);
        if (released != 1)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return false;
        }

        await TakeAsync(session.SiteId, session.Id, now, cancellationToken).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Stops holding, without anyone taking over: lost permission (FR-019).</summary>
    public Task ReleaseLockAsync(EditingSession session, CancellationToken cancellationToken) =>
        database.ForSite<EditingSession>(session.SiteId)
            .Where(s => s.Id == session.Id && s.HoldsLock)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.HoldsLock, false)
                    .SetProperty(e => e.LockTakenAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.LastActivityAt, (DateTimeOffset?)null),
                cancellationToken);

    /// <summary>Leaving the document (FR-015). Only the caller's own session; nothing when it is already gone.</summary>
    public Task<int> DeleteOwnAsync(Guid siteId, Guid sessionId, Guid userId, CancellationToken cancellationToken) =>
        database.ForSite<EditingSession>(siteId)
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task DeleteAsync(EditingSession session, CancellationToken cancellationToken) =>
        database.ForSite<EditingSession>(session.SiteId).Where(s => s.Id == session.Id).ExecuteDeleteAsync(cancellationToken);

    /// <summary>What <paramref name="session"/> shows: its role, the holder, its own loss of the lock, and the others.</summary>
    public async Task<PresenceResponse> PresenceAsync(EditingSession session, bool mayEdit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var live = await LiveAsync(session.SiteId, session.DocumentKind, session.DocumentId, now, cancellationToken).ConfigureAwait(false);
        var me = live.Find(s => s.Id == session.Id) ?? session;
        var holder = live.Find(s => s.HoldsLock);
        var others = live.Where(s => s.Id != me.Id).ToList();

        var names = await NamesAsync(
                live.Select(s => s.UserId).Append(me.UserId).Concat(me.LostLockToUserId is { } lostTo ? [lostTo] : []),
                cancellationToken)
            .ConfigureAwait(false);

        return new PresenceResponse(
            me.Id,
            now,
            me.HoldsLock ? "holder" : "viewer",
            mayEdit ? "edit" : "view-only",
            holder is null ? null : Lock(holder, me, names, now),
            me is { LostLockAt: { } at, LostLockToUserId: { } by, LostLockReason: { } reason }
                ? new LostLockResponse(at, Member(by, names), reason)
                : null,
            others.Select(s => new EditingOtherResponse(Member(s.UserId, names), s.OpenedAt, s.HoldsLock)).ToList());
    }

    /// <summary>The holder as a refusal names it.</summary>
    public async Task<(Guid Id, string Name, DateTimeOffset Since, bool Stale)> DescribeAsync(EditingSession holder, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var names = await NamesAsync([holder.UserId], cancellationToken).ConfigureAwait(false);
        return (holder.UserId, names.GetValueOrDefault(holder.UserId) ?? string.Empty, holder.LockTakenAt ?? holder.OpenedAt, holder.IsStale(now));
    }

    public async Task<HoldersResponse> HolderListAsync(Guid siteId, string kind, Guid? collectionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var holders = await HoldersAsync(siteId, kind, collectionId, now, cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(holders.Select(h => h.UserId), cancellationToken).ConfigureAwait(false);
        return new HoldersResponse(
            now,
            holders.Select(h => new HolderResponse(h.DocumentId, Member(h.UserId, names), h.LockTakenAt ?? h.OpenedAt, h.IsStale(now))).ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var found = await users.FindManyAsync(distinct, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(u => u.Id, u => u.DisplayName);
    }

    private static EditingLockResponse Lock(EditingSession holder, EditingSession me, IReadOnlyDictionary<Guid, string> names, DateTimeOffset now)
    {
        var inactive = holder.LastActivityAt is { } activity ? (int)Math.Max(0, (now - activity).TotalSeconds) : 0;
        return new EditingLockResponse(
            holder.Id,
            Member(holder.UserId, names),
            holder.LockTakenAt ?? holder.OpenedAt,
            holder.IsStale(now),
            inactive,
            holder.Id == me.Id,
            holder.Id != me.Id && holder.UserId == me.UserId);
    }

    /// <summary>The index refused a second holder; EF may or may not wrap the database error.</summary>
    private static bool IsHolderConflict(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: HolderIndex };

    private static EditingMemberResponse Member(Guid id, IReadOnlyDictionary<Guid, string> names) =>
        new(id, names.GetValueOrDefault(id) ?? string.Empty);

    private Task<int> TakeAsync(Guid siteId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.ForSite<EditingSession>(siteId)
            .Where(s => s.Id == sessionId && !s.HoldsLock)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.HoldsLock, true)
                    .SetProperty(e => e.LockTakenAt, now)
                    .SetProperty(e => e.LastActivityAt, now)
                    .SetProperty(e => e.LastSeenAt, now)
                    .SetProperty(e => e.LostLockAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.LostLockToUserId, (Guid?)null)
                    .SetProperty(e => e.LostLockReason, (string?)null),
                cancellationToken);
}
