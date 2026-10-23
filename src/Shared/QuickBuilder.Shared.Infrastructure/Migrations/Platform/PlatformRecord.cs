using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>
/// Reads the <c>platform</c> record under its advisory lock and runs the startup check
/// (019 contracts/installation-version.md §1). Shared by the migration runner and by the check
/// that runs before the host is built (<see cref="InstallationPreflight"/>).
/// </summary>
internal static class PlatformRecord
{
    /// <summary>The advisory lock guarding the record, as a module's guards its migrations.</summary>
    internal static readonly long LockKey = ModuleDatabase.DeriveLockKey(PlatformDbContext.Schema);

    /// <summary>
    /// Migrates the <c>platform</c> schema and decides. Always runs, even with
    /// <c>Database:AutoMigrate=false</c>: the schema is one additive table, and the database must
    /// be judged before anything else touches it. Nothing else is written.
    /// </summary>
    public static async Task<GateDecision> CheckAsync(PlatformDbContext context, ApplicationVersion version, ILogger logger, CancellationToken cancellationToken)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} migration(s) to the platform schema.", pending.Count);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        var row = await context.DatabaseVersion.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var state = row is null ? null : ToState(row);
        var hasModuleHistory = await HasModuleHistoryAsync(context, cancellationToken).ConfigureAwait(false);
        var decision = InstallationGate.Decide(version.Version, version.UpgradeFloor, state, hasModuleHistory);

        logger.LogInformation(
            "Database at {DatabaseVersion}; this is {Version} ({Outcome}).",
            Describe(state, hasModuleHistory),
            version.Version.ToString(),
            decision.Outcome);

        return decision;
    }

    /// <summary>Runs <paramref name="work"/> on <paramref name="context"/> holding the platform advisory lock.</summary>
    public static async Task<T> WithLockAsync<T>(
        PlatformDbContext context,
        TimeSpan timeout,
        Func<PlatformDbContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        await context.Database.OpenConnectionAsync(limit.Token).ConfigureAwait(false);
        try
        {
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", [LockKey], limit.Token).ConfigureAwait(false);
            return await work(context, limit.Token).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await context.Database
                    .ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [LockKey], CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    public static DatabaseVersionState ToState(DatabaseVersionRow row) =>
        new(
            SemanticVersion.Parse(row.RecordedVersion),
            row.UpgradingTo is null ? null : SemanticVersion.Parse(row.UpgradingTo));

    /// <summary>
    /// Whether any module has a migration history table: tells a fresh database from one a
    /// build before 0.1.0 wrote. Reads the catalogue only.
    /// </summary>
    private static async Task<bool> HasModuleHistoryAsync(PlatformDbContext context, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<bool>(
                $"""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_name = {PlatformDbContext.HistoryTable}
                      AND table_schema <> {PlatformDbContext.Schema}) AS "Value"
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

    private static string Describe(DatabaseVersionState? state, bool hasModuleHistory) =>
        state switch
        {
            null when hasModuleHistory => "a pre-release build",
            null => "nothing (fresh)",
            { Recorded: var r, UpgradingTo: { } m } when r == SemanticVersion.Zero => $"nothing yet (install of {m} interrupted)",
            { Recorded: var r, UpgradingTo: { } m } => $"{r} (upgrade to {m} interrupted)",
            { Recorded: var r } => r.ToString(),
        };
}
