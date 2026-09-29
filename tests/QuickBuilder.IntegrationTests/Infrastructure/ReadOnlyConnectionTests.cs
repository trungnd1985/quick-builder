using Npgsql;
using QuickBuilder.Shared.Infrastructure.Data;
using Xunit;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// PostgreSQL, not convention, keeps the public path read-only (012 research PR-06).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReadOnlyConnectionTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_write_through_a_read_only_connection_fails_in_the_database()
    {
        var connectionString = await database.CreateDatabaseAsync("readonly").ConfigureAwait(true);

        await using (var writer = new NpgsqlConnection(connectionString))
        {
            await writer.OpenAsync().ConfigureAwait(true);
            await using var create = new NpgsqlCommand("CREATE TABLE t (v int); INSERT INTO t VALUES (1);", writer);
            await create.ExecuteNonQueryAsync().ConfigureAwait(true);
        }

        await using var reader = new NpgsqlConnection(ReadOnlyConnection.From(connectionString));
        await reader.OpenAsync().ConfigureAwait(true);

        await using (var select = new NpgsqlCommand("SELECT count(*) FROM t", reader))
        {
            Assert.Equal(1L, await select.ExecuteScalarAsync().ConfigureAwait(true));
        }

        await using var insert = new NpgsqlCommand("INSERT INTO t VALUES (2)", reader);
        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync()).ConfigureAwait(true);

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, error.SqlState);
    }

    [Fact]
    public void Existing_options_are_kept()
    {
        var result = new NpgsqlConnectionStringBuilder(
            ReadOnlyConnection.From("Host=db;Database=x;Options=-c search_path=pages"));

        Assert.Contains("-c search_path=pages", result.Options, StringComparison.Ordinal);
        Assert.Contains("-c default_transaction_read_only=on", result.Options, StringComparison.Ordinal);
    }

    [Fact]
    public void Applying_it_twice_changes_nothing_more()
    {
        var once = ReadOnlyConnection.From("Host=db;Database=x");
        var twice = new NpgsqlConnectionStringBuilder(ReadOnlyConnection.From(once));

        Assert.Equal(1, twice.Options!.Split("default_transaction_read_only").Length - 1);
    }
}
