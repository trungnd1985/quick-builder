using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Migrations.Platform;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.Startup;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Shared.Infrastructure.Migrations;

/// <summary>
/// Applies every registered module's migrations, once, under a per-module advisory lock
/// (FR-023, FR-024, FR-030, D-04).
/// </summary>
/// <remarks>
/// 019: first migrates the <c>platform</c> schema and checks its version record
/// (<see cref="InstallationGate"/>), marks an upgrade before the first module migration, and
/// records the release after the last (contracts/installation-version.md §1).
/// </remarks>
public sealed class ModuleMigrationRunner(
    IServiceProvider services,
    IOptions<DatabaseOptions> options,
    StartupState startupState,
    ApplicationVersion version,
    TimeProvider timeProvider,
    ILogger<ModuleMigrationRunner> logger)
{
    private readonly DatabaseOptions _options = options.Value;

    /// <summary>
    /// Waits for the database, then migrates each module. Safe to call concurrently from
    /// several instances: the advisory lock serialises them and the losers find nothing
    /// to apply.
    /// </summary>
    public async Task RunAsync(bool force, CancellationToken cancellationToken)
    {
        var providers = services.GetServices<IModuleDatabaseProvider>().ToList();
        if (providers.Count == 0)
        {
            logger.LogWarning("No module databases are registered; nothing to migrate.");
            startupState.Advance(StartupPhase.Ready);
            return;
        }

        try
        {
            await WaitForDatabaseAsync(providers[0], cancellationToken).ConfigureAwait(false);

            // 019 RL-08: before the AutoMigrate decision, so the check runs either way.
            var decision = await CheckDatabaseVersionAsync(cancellationToken).ConfigureAwait(false);

            if (!force && !_options.AutoMigrate)
            {
                // FR-023: the operator chose to run migrations separately. The process is
                // alive and configured, but must not serve traffic against a schema that
                // may not match — so readiness stays unhealthy with a reason that says
                // exactly what to do.
                logger.LogInformation(
                    "Automatic migration is disabled (Database:AutoMigrate=false). Run the " +
                    "'migrate' command to apply migrations.");
                startupState.Advance(
                    StartupPhase.MigrationsPending,
                    "Automatic migration is disabled. Run the 'migrate' command.");
                return;
            }

            startupState.Advance(StartupPhase.Migrating);

            await MarkUpgradeAsync(decision, cancellationToken).ConfigureAwait(false);

            foreach (var provider in providers)
            {
                await MigrateModuleAsync(provider, cancellationToken).ConfigureAwait(false);
            }

            await RecordVersionAsync(cancellationToken).ConfigureAwait(false);

            startupState.Advance(StartupPhase.Ready);
            logger.LogInformation("All module migrations applied; application is ready.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Edge case "migration fails partway": refuse traffic, log the reason, leave
            // the database in a state that can be retried. Crashing would hide the cause
            // behind a restart loop.
            logger.LogError(ex, "Startup migration failed; the application will not become ready.");
            startupState.Advance(StartupPhase.Failed, "Migration failed. See logs.");
            throw;
        }
    }

    private async Task WaitForDatabaseAsync(IModuleDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.ConnectionRetryTimeout;
        var attempt = 0;

        while (true)
        {
            attempt++;
            using var scope = services.CreateScope();
            await using var context = provider.CreateContext(scope.ServiceProvider);

            try
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                logger.LogInformation("Database reachable after {Attempts} attempt(s).", attempt);
                return;
            }
            catch (DbException ex)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    logger.LogError(
                        ex,
                        "Database still unreachable after {Timeout}; giving up.",
                        _options.ConnectionRetryTimeout);
                    throw;
                }

                startupState.Advance(
                    StartupPhase.WaitingForDatabase,
                    $"Waiting for the database (attempt {attempt}).");

                logger.LogWarning(
                    "Database not reachable (attempt {Attempt}): {Reason}. Retrying in {Delay}.",
                    attempt,
                    ex.Message,
                    _options.ConnectionRetryDelay);

                await Task.Delay(_options.ConnectionRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Migrates the <c>platform</c> schema and decides, from its record, whether this build may
    /// migrate and run on the database (019 contracts/installation-version.md §1 steps 3–4).
    /// </summary>
    /// <remarks>
    /// Always runs, even with <c>Database:AutoMigrate=false</c>: the schema is one additive
    /// table, and the check must judge the database before anything else touches it. Nothing
    /// is written here beyond the <c>platform</c> migration itself.
    /// </remarks>
    private async Task<GateDecision> CheckDatabaseVersionAsync(CancellationToken cancellationToken)
    {
        return await WithPlatformLockAsync(
            async (context, token) =>
            {
                var pending = (await context.Database.GetPendingMigrationsAsync(token).ConfigureAwait(false)).ToList();
                if (pending.Count > 0)
                {
                    logger.LogInformation("Applying {Count} migration(s) to the platform schema.", pending.Count);
                    await context.Database.MigrateAsync(token).ConfigureAwait(false);
                }

                var state = await ReadStateAsync(context, token).ConfigureAwait(false);
                var hasModuleHistory = await HasModuleHistoryAsync(context, token).ConfigureAwait(false);
                var decision = InstallationGate.Decide(version.Version, version.UpgradeFloor, state, hasModuleHistory);

                logger.LogInformation(
                    "Database at {DatabaseVersion}; this is {Version} ({Outcome}).",
                    DescribeState(state, hasModuleHistory),
                    version.Version.ToString(),
                    decision.Outcome);

                return decision;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks the database as being upgraded before the first module migration, so an older
    /// release refuses it even if the upgrade stops halfway (FR-030, data-model §2).
    /// </summary>
    private async Task MarkUpgradeAsync(GateDecision decision, CancellationToken cancellationToken)
    {
        if (decision.MarkBefore is not { } mark)
        {
            return;
        }

        await WithPlatformLockAsync(
            async (context, token) =>
            {
                var now = timeProvider.GetUtcNow();
                var row = await context.DatabaseVersion.SingleOrDefaultAsync(token).ConfigureAwait(false);
                if (row is null)
                {
                    row = new DatabaseVersionRow { RecordedVersion = SemanticVersion.Zero.ToString(), RecordedAt = now };
                    context.DatabaseVersion.Add(row);
                }

                row.UpgradingTo = mark.ToString();
                row.UpgradingSince = now;
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// After every module migrated: records this release unless a higher one is already
    /// recorded (FR-027) and clears the upgrade mark. Writes nothing when nothing changes.
    /// </summary>
    private async Task RecordVersionAsync(CancellationToken cancellationToken)
    {
        var recorded = await WithPlatformLockAsync(
            async (context, token) =>
            {
                var row = await context.DatabaseVersion.SingleOrDefaultAsync(token).ConfigureAwait(false);
                if (row is null)
                {
                    // Only reachable if the row was deleted mid-run; record rather than guess.
                    row = new DatabaseVersionRow { RecordedVersion = SemanticVersion.Zero.ToString() };
                    context.DatabaseVersion.Add(row);
                }

                var state = ToState(row);
                if (InstallationGate.RecordAfter(version.Version, state) is { } higher)
                {
                    row.RecordedVersion = higher.ToString();
                    row.RecordedAt = timeProvider.GetUtcNow();
                }

                if (state.UpgradingTo is { } mark && mark.CompareMajorMinor(version.Version) <= 0)
                {
                    row.UpgradingTo = null;
                    row.UpgradingSince = null;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return row.RecordedVersion;
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Database at {DatabaseVersion}; running {Version}.", recorded, version.Version.ToString());
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a <c>platform</c> context holding the platform advisory
    /// lock, so several instances starting together read and write the record one at a time.
    /// </summary>
    private async Task<T> WithPlatformLockAsync<T>(
        Func<PlatformDbContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.MigrationTimeout);

        await context.Database.OpenConnectionAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            await context.Database
                .ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", [PlatformLockKey], timeout.Token)
                .ConfigureAwait(false);

            return await work(context, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await context.Database
                    .ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [PlatformLockKey], CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<DatabaseVersionState?> ReadStateAsync(PlatformDbContext context, CancellationToken cancellationToken)
    {
        var row = await context.DatabaseVersion.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToState(row);
    }

    private static DatabaseVersionState ToState(DatabaseVersionRow row) =>
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

    private static string DescribeState(DatabaseVersionState? state, bool hasModuleHistory) =>
        state switch
        {
            null when hasModuleHistory => "a pre-release build",
            null => "nothing (fresh)",
            { Recorded: var r, UpgradingTo: { } m } when r == SemanticVersion.Zero => $"nothing yet (install of {m} interrupted)",
            { Recorded: var r, UpgradingTo: { } m } => $"{r} (upgrade to {m} interrupted)",
            { Recorded: var r } => r.ToString(),
        };

    /// <summary>The advisory lock guarding the <c>platform</c> record, as a module's guards its migrations.</summary>
    private static readonly long PlatformLockKey = ModuleDatabase.DeriveLockKey(PlatformDbContext.Schema);

    private async Task MigrateModuleAsync(IModuleDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var description = provider.Describe();

        using var scope = services.CreateScope();
        await using var context = provider.CreateContext(scope.ServiceProvider);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.MigrationTimeout);

        // Session-level advisory lock. Held by the connection, so if this instance dies
        // the lock is released by the server — no stuck-lock cleanup path to write and
        // test. Other instances block here and then find nothing pending.
        await context.Database.OpenConnectionAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            logger.LogInformation(
                "Acquiring migration lock for module {Module} (schema {Schema}).",
                description.ModuleName,
                description.Schema);

            await context.Database
                .ExecuteSqlRawAsync(
                    "SELECT pg_advisory_lock({0})",
                    [description.AdvisoryLockKey],
                    timeout.Token)
                .ConfigureAwait(false);

            var pending = (await context.Database
                .GetPendingMigrationsAsync(timeout.Token)
                .ConfigureAwait(false)).ToList();

            if (pending.Count == 0)
            {
                logger.LogInformation("Module {Module} is already up to date.", description.ModuleName);
                return;
            }

            logger.LogInformation(
                "Applying {Count} migration(s) to module {Module}: {Migrations}",
                pending.Count,
                description.ModuleName,
                string.Join(", ", pending));

            await context.Database.MigrateAsync(timeout.Token).ConfigureAwait(false);

            logger.LogInformation("Module {Module} migrated.", description.ModuleName);
        }
        finally
        {
            try
            {
                await context.Database
                    .ExecuteSqlRawAsync(
                        "SELECT pg_advisory_unlock({0})",
                        [description.AdvisoryLockKey],
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }
}
