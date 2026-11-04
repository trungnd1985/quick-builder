using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Shared.Infrastructure.Data;

/// <summary>
/// Refuses content writes to a site while an import applies to it (020 FR-028, research ST-07).
/// </summary>
/// <remarks>
/// Every save that adds, changes or deletes rows of a site first takes a shared, transaction-scoped
/// advisory lock on that site, inside the save's transaction; when the save has none, it opens one
/// and commits it with the save. An import holds the same lock exclusively for its whole apply, so
/// a write already under way finishes before the import starts, and one that arrives during it is
/// refused with <see cref="SiteImportInProgressException"/>, answered as 423. The import's own saves
/// run on the connection that holds the lock, which PostgreSQL lets take it again.
/// <para>
/// Applies to the content modules' contexts. Set-based <c>ExecuteUpdate</c> and
/// <c>ExecuteDelete</c> statements do not pass through it; content writes go through the change
/// tracker (analysis U1).
/// </para>
/// </remarks>
public sealed class SiteWriteGateInterceptor : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, IDbContextTransaction> _opened = [];

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not { } context)
        {
            return result;
        }

        var sites = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && e.Entity is ISiteOwned)
            .Select(e => ((ISiteOwned)e.Entity).SiteId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (sites.Count == 0)
        {
            return result;
        }

        if (context.Database.CurrentTransaction is null)
        {
            _opened.AddOrUpdate(context, await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        }

        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        foreach (var site in sites)
        {
            if (!await SiteWriteLock.TryEnterSharedAsync(connection, transaction, site, cancellationToken).ConfigureAwait(false))
            {
                await RollBackOpenedAsync(context).ConfigureAwait(false);
                throw new SiteImportInProgressException(site);
            }
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && _opened.TryGetValue(context, out var opened))
        {
            _opened.Remove(context);
            await opened.CommitAsync(cancellationToken).ConfigureAwait(false);
            await opened.DisposeAsync().ConfigureAwait(false);
        }

        return result;
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            await RollBackOpenedAsync(context).ConfigureAwait(false);
        }
    }

    public override async Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            await RollBackOpenedAsync(context).ConfigureAwait(false);
        }
    }

    // Synchronous saves are not used by the modules' write paths; they pass ungated only for
    // rows of no site.
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        SavingChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
        SavedChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();

    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        SaveChangesFailedAsync(eventData).GetAwaiter().GetResult();

    private async Task RollBackOpenedAsync(DbContext context)
    {
        if (_opened.TryGetValue(context, out var opened))
        {
            _opened.Remove(context);
            await opened.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await opened.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>The per-site advisory lock that pauses content writes during an import (020 research ST-07).</summary>
public static class SiteWriteLock
{
    /// <summary>The 64-bit lock key of a site: FNV-1a over its id, stable across processes.</summary>
    public static long Key(Guid siteId)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var b in siteId.ToByteArray())
        {
            hash ^= b;
            hash *= prime;
        }

        // A namespace bit keeps site keys apart from the migration runner's module keys.
        return unchecked((long)(hash | 0x4000_0000_0000_0000UL));
    }

    /// <summary>Takes the site's lock for writing, shared, until <paramref name="transaction"/> ends. False when an import holds it.</summary>
    public static async Task<bool> TryEnterSharedAsync(DbConnection connection, DbTransaction transaction, Guid siteId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_try_advisory_xact_lock_shared(@key)";
        var key = command.CreateParameter();
        key.ParameterName = "key";
        key.Value = Key(siteId);
        command.Parameters.Add(key);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Takes the site's lock exclusively until <paramref name="transaction"/> ends, waiting for
    /// writes already under way. Only an import does this.
    /// </summary>
    public static async Task EnterExclusiveAsync(DbConnection connection, DbTransaction transaction, Guid siteId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        var key = command.CreateParameter();
        key.ParameterName = "key";
        key.Value = Key(siteId);
        command.Parameters.Add(key);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A content write refused because an import applies to its site (<c>site.import_in_progress</c>, 423).</summary>
public sealed class SiteImportInProgressException : Exception
{
    public const string Code = "site.import_in_progress";

    public SiteImportInProgressException()
    {
    }

    public SiteImportInProgressException(string message) : base(message)
    {
    }

    public SiteImportInProgressException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public SiteImportInProgressException(Guid siteId)
        : base($"Site {siteId} is being imported into; changes are paused until the import finishes.") => SiteId = siteId;

    public Guid SiteId { get; }
}
