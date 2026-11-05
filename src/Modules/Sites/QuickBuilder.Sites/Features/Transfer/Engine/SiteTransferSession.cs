using System.Data;
using System.Data.Common;
using Npgsql;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// The connection and transaction one export, check or import shares across every part
/// (constitution Principle I, as amended in 2.6.0; research ST-04, ST-05).
/// </summary>
/// <remarks>
/// Disposing without <see cref="CommitAsync"/> rolls back, which is how a failed, cancelled or
/// abandoned import leaves the target untouched (FR-018). If the process dies instead, the
/// server ends the connection and rolls back the same way.
/// </remarks>
internal sealed class SiteTransferSession : ISiteTransferSession, IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _finished;

    private SiteTransferSession(NpgsqlConnection connection, NpgsqlTransaction transaction, bool readOnly)
    {
        _connection = connection;
        _transaction = transaction;
        IsReadOnly = readOnly;
    }

    public DbConnection Connection => _connection;

    public DbTransaction Transaction => _transaction;

    public bool IsReadOnly { get; }

    /// <summary>A REPEATABLE READ, READ ONLY snapshot: every part of an export or check reads one committed state.</summary>
    public static Task<SiteTransferSession> BeginSnapshotAsync(string connectionString, CancellationToken cancellationToken) =>
        BeginAsync(connectionString, IsolationLevel.RepeatableRead, readOnly: true, cancellationToken);

    /// <summary>A READ COMMITTED write transaction, committed once after every part has applied.</summary>
    public static Task<SiteTransferSession> BeginWriteAsync(string connectionString, CancellationToken cancellationToken) =>
        BeginAsync(connectionString, IsolationLevel.ReadCommitted, readOnly: false, cancellationToken);

    private static async Task<SiteTransferSession> BeginAsync(
        string connectionString,
        IsolationLevel isolation,
        bool readOnly,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(isolation, cancellationToken).ConfigureAwait(false);
            if (readOnly)
            {
                await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return new SiteTransferSession(connection, transaction, readOnly);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Commits the import. Only the transfer engine calls this, once.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("A read-only transfer session has nothing to commit.");
        }

        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _finished = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_finished)
        {
            try
            {
                await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                // The connection is already broken; the server has rolled back.
            }
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
