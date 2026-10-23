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
/// Rules are applied in the order of data-model §2; the first that matches wins.
/// </remarks>
internal static class InstallationGate
{
    /// <summary>Where the operations guide explains what to do; linked from the log, never the probe.</summary>
    internal const string Guide = "https://github.com/trungnd1985/quick-builder/blob/main/docs/operations.md";

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
            // Rule 1: fresh. Rule 2: written before 0.1.0, upgradable while the floor is 0.0.0
            // (throughout 0.x, FR-030). Rule 3: from 1.0.0 on, older than any release.
            if (!hasModuleHistory)
            {
                return GateDecision.Pass(GateOutcome.Fresh, markBefore: running, insertRow: true);
            }

            return floor == SemanticVersion.Zero
                ? GateDecision.Pass(GateOutcome.PreRelease, markBefore: running, insertRow: true)
                : TooOld(running, floor, "a pre-release build");
        }

        if (state.UpgradingTo is { } mark)
        {
            // Rule 4: an upgrade to a newer minor started on this database.
            if (mark.CompareMajorMinor(running) > 0)
            {
                return Newer(running, mark);
            }

            // Rule 5: an install or upgrade to this major.minor stopped halfway; finish it.
            if (mark.CompareMajorMinor(running) == 0)
            {
                return GateDecision.Pass(GateOutcome.Resume, markBefore: null, insertRow: false);
            }
        }

        // Rule 6: a newer major.minor migrated this database.
        if (state.Recorded.CompareMajorMinor(running) > 0)
        {
            return Newer(running, state.Recorded);
        }

        // Rule 7: older than this build upgrades from directly.
        if (state.Recorded < floor)
        {
            return TooOld(running, floor, state.Recorded.ToString());
        }

        // Rule 8, and a leftover mark older than this build (which rule 8 never writes).
        if (running.CompareMajorMinor(state.Recorded) > 0 || state.UpgradingTo is not null)
        {
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

    private static GateDecision Newer(SemanticVersion running, SemanticVersion database) =>
        GateDecision.Refuse(
            GateOutcome.RefuseNewer,
            new GateRefusal(
                $"Database was migrated by QuickBuilder {database}; this is {running}, which cannot run on it. " +
                $"Start {database} or newer, or restore the backup taken before upgrading: {Guide}#rolling-back",
                "This database was upgraded by a newer QuickBuilder release. Start that release, or restore the backup taken before the upgrade. See the logs."));

    private static GateDecision TooOld(SemanticVersion running, SemanticVersion floor, string database) =>
        GateDecision.Refuse(
            GateOutcome.RefuseTooOld,
            new GateRefusal(
                $"Database was last migrated by QuickBuilder {database}; {running} upgrades from {floor} or newer. " +
                $"Upgrade to {floor} first, then to {running}: {Guide}#upgrade-path",
                "This database is too old for this release to upgrade directly. See the logs."));
}
