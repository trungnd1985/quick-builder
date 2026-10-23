using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>What <c>platform.database_version</c> said when the check read it.</summary>
/// <param name="Recorded">The highest release that completed migrating the database.</param>
/// <param name="UpgradingTo">The release an upgrade started for and has not finished, if any.</param>
internal sealed record DatabaseVersionState(SemanticVersion Recorded, SemanticVersion? UpgradingTo);

/// <summary>
/// Decides whether this build may migrate and run on a database, and what to record
/// (019 data-model §2, research RL-08, FR-027 – FR-031).
/// </summary>
/// <remarks>
/// A pure function of four inputs, so every row of the decision table is a unit test. The
/// runner reads the inputs and performs the writes under the <c>platform</c> advisory lock.
/// "Newer" compares major.minor only: a patch never changes stored data (clarification Q1).
/// </remarks>
internal static class InstallationGate
{
    /// <param name="running">The version of this build.</param>
    /// <param name="floor">The oldest release this build upgrades from.</param>
    /// <param name="state">The row, or null when the table is empty.</param>
    /// <param name="hasModuleHistory">Whether any module has migration history: tells a fresh
    /// database from one a pre-release build wrote.</param>
    public static GateDecision Decide(
        SemanticVersion running,
        SemanticVersion floor,
        DatabaseVersionState? state,
        bool hasModuleHistory)
    {
        if (state is null)
        {
            // Rules 1 and 2: fresh, or written before 0.1.0 (no record yet).
            return GateDecision.Pass(
                hasModuleHistory ? GateOutcome.PreRelease : GateOutcome.Fresh,
                markBefore: running,
                insertRow: true);
        }

        if (state.UpgradingTo is { } mark && mark.CompareMajorMinor(running) == 0)
        {
            // Rule 5: an install or upgrade to this major.minor stopped halfway; finish it.
            return GateDecision.Pass(GateOutcome.Resume, markBefore: null, insertRow: false);
        }

        if (running.CompareMajorMinor(state.Recorded) > 0 || state.UpgradingTo is not null)
        {
            // Rule 8, and a mark older than this build (which rule 8 never writes): an upgrade.
            return GateDecision.Pass(GateOutcome.Upgrade, markBefore: running, insertRow: false);
        }

        // Rule 9: the same major.minor, either direction (1.4.2 on a database 1.4.3 migrated).
        return GateDecision.Pass(GateOutcome.Same, markBefore: null, insertRow: false);
    }

    /// <summary>
    /// The version to record after every module migrated: never lower than what is there
    /// (FR-027), and null when nothing would change.
    /// </summary>
    public static SemanticVersion? RecordAfter(SemanticVersion running, DatabaseVersionState current) =>
        running > current.Recorded ? running : null;
}
