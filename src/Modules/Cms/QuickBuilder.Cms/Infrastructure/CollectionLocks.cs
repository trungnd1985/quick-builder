using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// The only place the Cms module takes row locks (research CS-09, CS-10).
/// </summary>
/// <remarks>
/// <para>
/// A schema change or a delete takes <c>FOR UPDATE NOWAIT</c> on the collection's row; an item
/// write takes <c>FOR SHARE NOWAIT</c> through <see cref="CollectionWriteGate"/>. Each waits
/// for nothing: a lock already held makes the second caller fail at once, which is how a save
/// during a migration, a second migration and a delete during a migration are all refused
/// (clarification Q3, FR-037). Locks last until the caller's transaction ends, so the caller
/// must have one open.
/// </para>
/// <para>
/// The query returns the revision only, so no entity and no global query filter is involved.
/// Its <c>site_id</c> predicate takes the filter's place and must stay;
/// <c>CmsSiteFilterTests</c> proves another site's collection is never locked. The values are
/// parameters, never text.
/// </para>
/// </remarks>
internal sealed class CollectionLocks(CmsDbContext database)
{
    private const string LockNotAvailable = "55P03";

    /// <summary>Locks the collection for a schema change or a delete; returns its current revision.</summary>
    public Task<Result<int>> LockForUpdateAsync(Guid siteId, Guid collectionId, CancellationToken cancellationToken) =>
        LockAsync(
            database.Database.SqlQuery<int>(
                $"SELECT revision AS \"Value\" FROM cms.collections WHERE id = {collectionId} AND site_id = {siteId} FOR UPDATE NOWAIT"),
            CmsErrors.CollectionBusy,
            cancellationToken);

    /// <summary>
    /// Locks the collection against schema changes while an item is written; returns its
    /// current revision. Fails with <paramref name="busy"/> while a change holds it.
    /// </summary>
    public Task<Result<int>> LockForShareAsync(Guid siteId, Guid collectionId, Error busy, CancellationToken cancellationToken) =>
        LockAsync(
            database.Database.SqlQuery<int>(
                $"SELECT revision AS \"Value\" FROM cms.collections WHERE id = {collectionId} AND site_id = {siteId} FOR SHARE NOWAIT"),
            busy,
            cancellationToken);

    private async Task<Result<int>> LockAsync(IQueryable<int> query, Error busy, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A collection lock lasts until its transaction ends; begin one first.");
        }

        try
        {
            var revisions = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            return revisions.Count == 0 ? Result.Failure<int>(CmsErrors.CollectionNotFound) : Result.Success(revisions[0]);
        }
        catch (Exception ex) when (IsLockNotAvailable(ex))
        {
            return Result.Failure<int>(busy);
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is PostgreSQL refusing a <c>NOWAIT</c> lock. EF Core may
    /// wrap it (as a possibly transient failure), so the whole chain is searched.
    /// </summary>
    private static bool IsLockNotAvailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: LockNotAvailable })
            {
                return true;
            }
        }

        return false;
    }
}
