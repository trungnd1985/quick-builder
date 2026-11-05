using Npgsql;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;

namespace QuickBuilder.IntegrationTests.Transfer;

/// <summary>
/// The per-site advisory lock that pauses content writes while an import applies (020 FR-028,
/// research ST-07), on real PostgreSQL connections.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransferLockTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_write_is_refused_while_an_import_holds_the_site_and_allowed_after()
    {
        var connectionString = await database.CreateDatabaseAsync("lock-refuse").ConfigureAwait(true);
        var site = Guid.NewGuid();
        await using var import = await OpenAsync(connectionString).ConfigureAwait(true);
        await using var importTransaction = await import.BeginTransactionAsync().ConfigureAwait(true);
        await SiteWriteLock.EnterExclusiveAsync(import, importTransaction, site, CancellationToken.None).ConfigureAwait(true);

        Assert.False(await TryWriteAsync(connectionString, site).ConfigureAwait(true));
        Assert.True(await TryWriteAsync(connectionString, Guid.NewGuid()).ConfigureAwait(true));

        await importTransaction.CommitAsync().ConfigureAwait(true);
        Assert.True(await TryWriteAsync(connectionString, site).ConfigureAwait(true));
    }

    [Fact]
    public async Task An_import_waits_for_a_write_already_under_way_to_commit()
    {
        var connectionString = await database.CreateDatabaseAsync("lock-wait").ConfigureAwait(true);
        var site = Guid.NewGuid();
        await using var write = await OpenAsync(connectionString).ConfigureAwait(true);
        await using var writeTransaction = await write.BeginTransactionAsync().ConfigureAwait(true);
        Assert.True(await SiteWriteLock.TryEnterSharedAsync(write, writeTransaction, site, CancellationToken.None).ConfigureAwait(true));

        await using var import = await OpenAsync(connectionString).ConfigureAwait(true);
        await using var importTransaction = await import.BeginTransactionAsync().ConfigureAwait(true);
        var entering = SiteWriteLock.EnterExclusiveAsync(import, importTransaction, site, CancellationToken.None);

        // The gated write holds the shared lock until its transaction ends.
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(true);
        Assert.False(entering.IsCompleted);

        await writeTransaction.CommitAsync().ConfigureAwait(true);
        await entering.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        Assert.False(await TryWriteAsync(connectionString, site).ConfigureAwait(true));
    }

    [Fact]
    public async Task The_lock_ends_with_the_import_s_connection()
    {
        var connectionString = await database.CreateDatabaseAsync("lock-dies").ConfigureAwait(true);
        var site = Guid.NewGuid();
        var import = await OpenAsync(connectionString).ConfigureAwait(true);
        var importTransaction = await import.BeginTransactionAsync().ConfigureAwait(true);
        await SiteWriteLock.EnterExclusiveAsync(import, importTransaction, site, CancellationToken.None).ConfigureAwait(true);
        Assert.False(await TryWriteAsync(connectionString, site).ConfigureAwait(true));

        // A crashed worker: its backend goes, and with it every lock it held.
        await using (var admin = await OpenAsync(connectionString).ConfigureAwait(true))
        await using (var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", admin))
        {
            kill.Parameters.AddWithValue("pid", import.ProcessID);
            await kill.ExecuteScalarAsync().ConfigureAwait(true);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(true);
        Assert.True(await TryWriteAsync(connectionString, site).ConfigureAwait(true));

        try
        {
            await importTransaction.DisposeAsync().ConfigureAwait(true);
            await import.DisposeAsync().ConfigureAwait(true);
        }
        catch (NpgsqlException)
        {
            // The connection was killed on purpose.
        }
    }

    [Fact]
    public void Sites_get_distinct_stable_keys_apart_from_the_migration_runner_s()
    {
        var site = Guid.NewGuid();
        Assert.Equal(SiteWriteLock.Key(site), SiteWriteLock.Key(site));
        Assert.NotEqual(SiteWriteLock.Key(site), SiteWriteLock.Key(Guid.NewGuid()));
        Assert.NotEqual(0, SiteWriteLock.Key(site) & 0x4000_0000_0000_0000L);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    /// <summary>What the write gate does for one save: the shared lock, in its own transaction.</summary>
    private static async Task<bool> TryWriteAsync(string connectionString, Guid site)
    {
        await using var connection = await OpenAsync(connectionString).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var entered = await SiteWriteLock.TryEnterSharedAsync(connection, transaction, site, CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return entered;
    }
}
