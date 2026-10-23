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
}

/// <summary>The check's answer: pass, and what to write before the modules migrate.</summary>
internal sealed record GateDecision(GateOutcome Outcome, SemanticVersion? MarkBefore, bool InsertRow)
{
    public static GateDecision Pass(GateOutcome outcome, SemanticVersion? markBefore, bool insertRow) =>
        new(outcome, markBefore, insertRow);
}
