using System.ComponentModel.DataAnnotations;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Shared.Infrastructure.Migrations;

/// <summary>
/// Database configuration (FR-023, FR-029, FR-030).
/// </summary>
/// <remarks>
/// Validated on start (FR-028) so an invalid value stops the process before it listens,
/// naming the offending key, rather than surfacing as a confusing runtime failure.
/// </remarks>
[SettingsSection(SectionName, Area = "database", Title = "Database")]
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Apply migrations automatically during startup (FR-023). Default true — the
    /// five-minute install promise depends on it.
    /// </summary>
    /// <remarks>
    /// Set false when an operator would rather run migrations as a separate, controlled
    /// step with the <c>migrate</c> command. Readiness then reports MigrationsPending
    /// until they do, so a half-deployed installation is visible rather than silent.
    /// </remarks>
    [Setting("Apply database migrations during startup. Set `false` to apply them yourself with the `migrate` command; readiness then reports `MigrationsPending` until you do, so a half-deployed installation is visible.")]
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// How long to keep retrying an unreachable database during startup (FR-030).
    /// </summary>
    /// <remarks>
    /// Bounded rather than infinite: a container that waits forever looks identical to
    /// one that is wedged. Readiness reports WaitingForDatabase throughout.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00", "00:30:00", ErrorMessage = "Database:ConnectionRetryTimeout must be between {1} and {2}.")]
    [Setting("How long to keep retrying an unreachable database at startup. Readiness reports `WaitingForDatabase` meanwhile.")]
    public TimeSpan ConnectionRetryTimeout { get; set; } = TimeSpan.FromMinutes(2);

    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00", ErrorMessage = "Database:ConnectionRetryDelay must be between {1} and {2}.")]
    [Setting("The wait between database connection attempts at startup.")]
    public TimeSpan ConnectionRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long one module may hold its migration advisory lock.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:30:00", ErrorMessage = "Database:MigrationTimeout must be between {1} and {2}.")]
    [Setting("How long one module may hold its migration lock, so a stuck migration fails instead of waiting forever.")]
    public TimeSpan MigrationTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
