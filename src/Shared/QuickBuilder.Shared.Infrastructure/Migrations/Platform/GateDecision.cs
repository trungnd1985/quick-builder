using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>How the check classified the database.</summary>
internal enum GateOutcome
{
    /// <summary>Rule 1: an empty database.</summary>
    Fresh,

    /// <summary>Rule 2: module tables but no record, as builds before 0.1.0 leave it.</summary>
    PreRelease,

    /// <summary>Rule 5: an interrupted install or upgrade to this major.minor.</summary>
    Resume,

    /// <summary>Rule 8: this build is a newer major.minor.</summary>
    Upgrade,

    /// <summary>Rule 9: the same major.minor.</summary>
    Same,

    /// <summary>Rules 4 and 6: a newer major.minor migrated, or started migrating, this database.</summary>
    RefuseNewer,

    /// <summary>Rules 3 and 7: older than this build upgrades from directly.</summary>
    RefuseTooOld,
}

/// <summary>Why the check refused, for the operator.</summary>
/// <param name="LogMessage">Logged once at Critical: both versions and what to do, with a link to the guide.</param>
/// <param name="Detail">On the anonymous readiness probe: the reason and the next step, no version numbers (Principle IX).</param>
internal sealed record GateRefusal(string LogMessage, string Detail);

/// <summary>The check's answer: pass and what to write before the modules migrate, or refuse.</summary>
internal sealed record GateDecision(GateOutcome Outcome, SemanticVersion? MarkBefore, bool InsertRow, GateRefusal? Refusal)
{
    public static GateDecision Pass(GateOutcome outcome, SemanticVersion? markBefore, bool insertRow) =>
        new(outcome, markBefore, insertRow, Refusal: null);

    public static GateDecision Refuse(GateOutcome outcome, GateRefusal refusal) =>
        new(outcome, MarkBefore: null, InsertRow: false, refusal);
}
