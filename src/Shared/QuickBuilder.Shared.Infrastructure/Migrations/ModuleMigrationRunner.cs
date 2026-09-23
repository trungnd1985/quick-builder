using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.Startup;

namespace QuickBuilder.Shared.Infrastructure.Migrations;

/// <summary>
/// Applies every registered module's migrations, once, under a per-module advisory lock
/// (FR-023, FR-024, FR-030, D-04).
/// </summary>
public sealed class ModuleMigrationRunner(
    IServiceProvider services,
    IOptions<DatabaseOptions> options,
    StartupState startupState,
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

            foreach (var provider in providers)
            {
                await MigrateModuleAsync(provider, cancellationToken).ConfigureAwait(false);
            }

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
