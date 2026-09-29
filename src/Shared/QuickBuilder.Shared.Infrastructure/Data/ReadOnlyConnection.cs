using Npgsql;

namespace QuickBuilder.Shared.Infrastructure.Data;

/// <summary>
/// Connection strings whose sessions PostgreSQL itself keeps read-only (012 research PR-06).
/// </summary>
/// <remarks>
/// Constitution Principle VII: the public rendering path must not have write access to
/// content tables. The readers that serve it connect through a string built here. It sets
/// <c>default_transaction_read_only</c> for the session, so any write, however it is
/// attempted, fails in the database with SQLSTATE <c>25006</c>. The operator's credentials
/// stay the same; nothing new is configured.
/// </remarks>
public static class ReadOnlyConnection
{
    private const string ReadOnlyOption = "-c default_transaction_read_only=on";

    /// <summary>The same connection string with read-only sessions, keeping any existing options.</summary>
    public static string From(string? connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString ?? string.Empty);

        if (builder.Options?.Contains("default_transaction_read_only", StringComparison.Ordinal) != true)
        {
            builder.Options = string.IsNullOrWhiteSpace(builder.Options)
                ? ReadOnlyOption
                : $"{builder.Options} {ReadOnlyOption}";
        }

        // A separate pool, so a read-only session is never handed to a writer.
        builder.ApplicationName = string.IsNullOrEmpty(builder.ApplicationName)
            ? "quickbuilder-readonly"
            : $"{builder.ApplicationName}-readonly";

        return builder.ConnectionString;
    }
}
