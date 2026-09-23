using System.ComponentModel.DataAnnotations;

namespace QuickBuilder.Shared.Infrastructure.Migrations;

/// <summary>
/// Database configuration (FR-023, FR-029, FR-030).
/// </summary>
/// <remarks>
/// Validated on start (FR-028) so an invalid value stops the process before it listens,
/// naming the offending key, rather than surfacing as a confusing runtime failure.
/// </remarks>
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
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// How long to keep retrying an unreachable database during startup (FR-030).
    /// </summary>
    /// <remarks>
    /// Bounded rather than infinite: a container that waits forever looks identical to
    /// one that is wedged. Readiness reports WaitingForDatabase throughout.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00", "00:30:00")]
    public TimeSpan ConnectionRetryTimeout { get; set; } = TimeSpan.FromMinutes(2);

    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan ConnectionRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long one module may hold its migration advisory lock.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:30:00")]
    public TimeSpan MigrationTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
