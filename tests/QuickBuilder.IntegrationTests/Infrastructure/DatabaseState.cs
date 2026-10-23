using System.Globalization;
using Npgsql;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// Reads a whole database's state for "nothing was written" assertions (019 SC-006, SC-007).
/// </summary>
/// <remarks>
/// Each table's fingerprint hashes every row together with its <c>xmin</c>, so an insert, a
/// delete and even an update that writes the same values all change it. The statistics views
/// (<c>pg_stat_user_tables</c>) would be cheaper but are flushed asynchronously from
/// PostgreSQL 15 on, so a write can be invisible to them for seconds.
/// </remarks>
internal static class DatabaseState
{
    /// <summary>Every user table, by <c>schema.table</c>, mapped to a hash of its rows.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> FingerprintAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            """
            SELECT quote_ident(table_schema) || '.' || quote_ident(table_name)
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
              AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY 1
            """,
            connection))
        await using (var reader = await list.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            // The names come from the catalogue above and are quoted there, not from input.
#pragma warning disable CA2100
            await using var hash = new NpgsqlCommand(
                $"SELECT count(*) || ':' || coalesce(md5(string_agg(t.xmin::text || t::text, '|' ORDER BY t.xmin::text || t::text)), '') FROM {table} t",
                connection);
#pragma warning restore CA2100
            fingerprints[table] = (string)(await hash.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        return fingerprints;
    }

    /// <summary>The tables whose fingerprint differs, or that exist in only one of the two.</summary>
    public static IReadOnlyList<string> Changed(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return [.. before.Keys.Union(after.Keys)
            .Where(t => !before.TryGetValue(t, out var b) || !after.TryGetValue(t, out var a) || a != b)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The row of <c>platform.database_version</c>, or null when there is none.</summary>
    public static async Task<VersionRow?> ReadVersionAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var exists = new NpgsqlCommand(
            "SELECT to_regclass('platform.database_version') IS NOT NULL", connection);
        if (!(bool)(await exists.ExecuteScalarAsync().ConfigureAwait(false))!)
        {
            return null;
        }

        await using var command = new NpgsqlCommand(
            "SELECT recorded_version, upgrading_to FROM platform.database_version WHERE id = 1", connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        return await reader.ReadAsync().ConfigureAwait(false)
            ? new VersionRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1))
            : null;
    }

    /// <summary>Writes the row directly, as another release would have left it.</summary>
    public static async Task WriteVersionAsync(string connectionString, string recorded, string? upgradingTo = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.database_version (id, recorded_version, recorded_at, upgrading_to, upgrading_since)
            VALUES (1, @recorded, now(), @upgrading, CASE WHEN @upgrading IS NULL THEN NULL ELSE now() END)
            ON CONFLICT (id) DO UPDATE
              SET recorded_version = excluded.recorded_version,
                  upgrading_to = excluded.upgrading_to,
                  upgrading_since = excluded.upgrading_since
            """,
            connection);
        command.Parameters.AddWithValue("recorded", recorded);
        command.Parameters.Add(new NpgsqlParameter<string?>("upgrading", upgradingTo));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>Runs one statement, for setting up a database as an older build left it.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
#pragma warning disable CA2100 // Test set-up statements are constants in the tests.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>A scalar count, for assertions.</summary>
    public static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
#pragma warning disable CA2100 // Test assertions are constants in the tests.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    internal sealed record VersionRow(string RecordedVersion, string? UpgradingTo);
}
