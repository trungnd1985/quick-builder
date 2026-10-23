namespace QuickBuilder.Shared.Infrastructure.Startup;

/// <summary>Where the application is in its startup sequence (D-17).</summary>
public enum StartupPhase
{
    /// <summary>The database is not reachable yet; still retrying (FR-030).</summary>
    WaitingForDatabase,

    /// <summary>Migrations are being applied.</summary>
    Migrating,

    /// <summary>
    /// Automatic migration is disabled and migrations have not been applied. The process
    /// is healthy but deliberately not ready — an operator must run the migrate command
    /// (FR-023).
    /// </summary>
    MigrationsPending,

    /// <summary>Configuration valid, migrations complete, database reachable.</summary>
    Ready,

    /// <summary>
    /// Startup failed. Readiness stays unhealthy rather than the process crash-looping,
    /// so an operator can read the reason from the probe and the logs.
    /// </summary>
    Failed,

    /// <summary>
    /// 019 RL-08: the database belongs to a newer release, or is too old to upgrade directly.
    /// Nothing was changed; only the refusal host answers, and the log says what to do.
    /// </summary>
    Incompatible,
}

/// <summary>
/// Singleton the readiness probe reads (FR-027).
/// </summary>
/// <remarks>
/// Liveness and readiness answer different questions: liveness is "can this process serve
/// requests at all", readiness is "should traffic be sent to it". Splitting them along
/// this state is what lets a database outage turn readiness unhealthy while liveness stays
/// healthy, instead of an orchestrator killing a process that is fine (SC-010).
/// </remarks>
public sealed class StartupState
{
    private readonly Lock _gate = new();
    private StartupPhase _phase = StartupPhase.WaitingForDatabase;
    private string? _detail;

    public StartupPhase Phase
    {
        get { lock (_gate) { return _phase; } }
    }

    /// <summary>Human-readable context for the current phase, safe to expose.</summary>
    public string? Detail
    {
        get { lock (_gate) { return _detail; } }
    }

    public bool IsReady => Phase == StartupPhase.Ready;

    public void Advance(StartupPhase phase, string? detail = null)
    {
        lock (_gate)
        {
            _phase = phase;
            _detail = detail;
        }
    }
}
