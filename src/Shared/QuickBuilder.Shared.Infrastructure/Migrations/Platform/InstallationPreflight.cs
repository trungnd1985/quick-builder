using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>
/// The startup check, run before the application host is built (019 research RL-08,
/// contracts/installation-version.md §3).
/// </summary>
/// <remarks>
/// Running before the host exists is what lets a refusal start nothing: the host removes every
/// hosted service and serves only the refusal pipeline, so no module code, outbox dispatcher,
/// schedule or sweep ever touches a database this release does not understand. The migration
/// runner checks again after the host is built (it serves the <c>migrate</c> command and closes
/// any race); this check only decides whether the host starts at all.
/// <para>
/// It answers "no refusal" whenever it cannot decide — no connection string, a database that
/// never answers, any other failure — and leaves reporting those to the runner, which already
/// does (readiness <c>WaitingForDatabase</c> or <c>Failed</c>).
/// </para>
/// </remarks>
public static class InstallationPreflight
{
    /// <summary>The refusal's anonymous detail, or null when the host may start normally.</summary>
    /// <returns>The refusal, with the message to log once the host's logger exists.</returns>
    public static async Task<(string LogMessage, string Detail)?> CheckAsync(
        IConfiguration configuration,
        ApplicationVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(version);

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        await using var context = new PlatformDbContext(
            new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.Schema))
                .Options);

        try
        {
            if (!await WaitForDatabaseAsync(context, options, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var decision = await PlatformRecord
                .WithLockAsync(context, options.MigrationTimeout, (c, t) => PlatformRecord.CheckAsync(c, version, NullLogger.Instance, t), cancellationToken)
                .ConfigureAwait(false);

            return decision.Refusal is { } refusal ? (refusal.LogMessage, refusal.Detail) : null;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    private static async Task<bool> WaitForDatabaseAsync(PlatformDbContext context, DatabaseOptions options, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + options.ConnectionRetryTimeout;
        while (true)
        {
            try
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                return true;
            }
            catch (DbException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(options.ConnectionRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (DbException)
            {
                return false;
            }
        }
    }
}
