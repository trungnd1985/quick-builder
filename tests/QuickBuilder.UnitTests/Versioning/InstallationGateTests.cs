using QuickBuilder.Shared.Infrastructure.Migrations.Platform;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.UnitTests.Versioning;

/// <summary>
/// Every row of the compatibility table (019 data-model §2; FR-027 – FR-031, clarification Q1).
/// </summary>
/// <remarks>
/// The passing rows have held since the record was introduced (T012); the refusal rows are the
/// startup check proper. "Newer" compares major.minor only.
/// </remarks>
public sealed class InstallationGateTests
{
    private static readonly SemanticVersion Zero = SemanticVersion.Zero;

    private static SemanticVersion V(string text) => SemanticVersion.Parse(text);

    private static DatabaseVersionState Row(string recorded, string? upgradingTo = null) =>
        new(V(recorded), upgradingTo is null ? null : V(upgradingTo));

    private static GateDecision Decide(string running, DatabaseVersionState? row, bool history = true, string floor = "0.0.0") =>
        InstallationGate.Decide(V(running), V(floor), row, history);

    [Fact]
    public void Rule_1_a_fresh_database_installs_and_is_marked_first()
    {
        var decision = Decide("0.2.0", null, history: false);

        Assert.Equal(GateOutcome.Fresh, decision.Outcome);
        Assert.Equal(V("0.2.0"), decision.MarkBefore);
        Assert.True(decision.InsertRow);
        Assert.Null(decision.Refusal);
    }

    [Fact]
    public void Rule_2_a_pre_release_database_upgrades_throughout_0x()
    {
        var decision = Decide("0.3.0", null);

        Assert.Equal(GateOutcome.PreRelease, decision.Outcome);
        Assert.True(decision.InsertRow);
    }

    [Fact]
    public void Rule_3_a_pre_release_database_is_too_old_once_the_floor_is_a_release()
    {
        var decision = Decide("1.0.0", null, floor: "0.7.0");

        Assert.Equal(GateOutcome.RefuseTooOld, decision.Outcome);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("a pre-release build", decision.Refusal.LogMessage, StringComparison.Ordinal);
        Assert.Contains("0.7.0", decision.Refusal.LogMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_4_an_unfinished_upgrade_to_a_newer_minor_is_refused()
    {
        var decision = Decide("0.2.0", Row("0.2.0", upgradingTo: "0.3.0"));

        Assert.Equal(GateOutcome.RefuseNewer, decision.Outcome);
        Assert.Contains("0.3.0", decision.Refusal!.LogMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("0.3.0", "0.3.0-rc.1")]
    [InlineData("0.3.1", "0.3.0")]
    public void Rule_5_an_unfinished_upgrade_to_this_minor_is_resumed(string running, string mark)
    {
        var decision = Decide(running, Row("0.2.0", upgradingTo: mark));

        Assert.Equal(GateOutcome.Resume, decision.Outcome);
        Assert.Null(decision.Refusal);
    }

    [Fact]
    public void Rule_5_comes_before_rule_7_so_an_interrupted_first_install_is_finished()
    {
        var decision = Decide("1.0.0", Row("0.0.0", upgradingTo: "1.0.0"), floor: "0.7.0");

        Assert.Equal(GateOutcome.Resume, decision.Outcome);
    }

    [Theory]
    [InlineData("0.2.0", "0.3.0")]
    [InlineData("1.3.0", "1.4.0")]
    [InlineData("1.9.9", "2.0.0")]
    [InlineData("0.3.0-dev", "0.4.0")]
    public void Rule_6_a_database_from_a_newer_minor_or_major_is_refused(string running, string recorded)
    {
        var decision = Decide(running, Row(recorded));

        Assert.Equal(GateOutcome.RefuseNewer, decision.Outcome);
        Assert.Contains(recorded, decision.Refusal!.LogMessage, StringComparison.Ordinal);
        Assert.Contains(running, decision.Refusal.LogMessage, StringComparison.Ordinal);
        // 026 OD-11: the published upgrade guide of the running release line.
        var line = $"{V(running).Major}.{V(running).Minor}";
        Assert.Contains($"{ApplicationVersion.DocsBaseUrl}{line}/upgrade#database-newer", decision.Refusal.LogMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com", decision.Refusal.LogMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_7_a_database_older_than_the_floor_is_refused_and_names_the_floor()
    {
        var decision = Decide("2.1.0", Row("1.2.0"), floor: "1.5.0");

        Assert.Equal(GateOutcome.RefuseTooOld, decision.Outcome);
        Assert.Contains("1.2.0", decision.Refusal!.LogMessage, StringComparison.Ordinal);
        Assert.Contains("1.5.0", decision.Refusal.LogMessage, StringComparison.Ordinal);
        Assert.Contains($"{ApplicationVersion.DocsBaseUrl}2.1/upgrade#database-too-old", decision.Refusal.LogMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_7_a_patch_of_the_floor_is_not_too_old()
    {
        Assert.Equal(GateOutcome.Upgrade, Decide("2.1.0", Row("1.5.3"), floor: "1.5.0").Outcome);
    }

    [Fact]
    public void Rule_8_a_newer_minor_upgrades_and_marks_the_database_first()
    {
        var decision = Decide("0.3.0", Row("0.2.4"));

        Assert.Equal(GateOutcome.Upgrade, decision.Outcome);
        Assert.Equal(V("0.3.0"), decision.MarkBefore);
        Assert.False(decision.InsertRow);
    }

    [Fact]
    public void Rule_8_a_leftover_mark_older_than_this_build_is_treated_as_an_upgrade()
    {
        var decision = Decide("0.4.0", Row("0.3.0", upgradingTo: "0.2.0"));

        Assert.Equal(GateOutcome.Upgrade, decision.Outcome);
        Assert.Equal(V("0.4.0"), decision.MarkBefore);
    }

    [Theory]
    [InlineData("1.4.2", "1.4.3")]
    [InlineData("1.4.3", "1.4.2")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("0.2.0", "0.2.0-dev")]
    [InlineData("0.2.0-dev", "0.2.0")]
    public void Rule_9_the_same_minor_runs_either_way_and_writes_nothing_before(string running, string recorded)
    {
        var decision = Decide(running, Row(recorded));

        Assert.Equal(GateOutcome.Same, decision.Outcome);
        Assert.Null(decision.MarkBefore);
        Assert.Null(decision.Refusal);
    }

    [Theory]
    [InlineData("1.4.2", "1.4.3", null)]
    [InlineData("1.4.3", "1.4.2", "1.4.3")]
    [InlineData("0.2.0", "0.2.0-dev", "0.2.0")]
    public void The_record_is_never_lowered(string running, string recorded, string? expected)
    {
        var after = InstallationGate.RecordAfter(V(running), Row(recorded));

        Assert.Equal(expected is null ? null : V(expected), after);
    }

    [Fact]
    public void The_anonymous_detail_never_names_a_version()
    {
        foreach (var decision in new[]
        {
            Decide("0.2.0", Row("0.3.0")),
            Decide("0.2.0", Row("0.2.0", upgradingTo: "0.3.0")),
            Decide("2.0.0", Row("1.0.0"), floor: "1.4.0"),
            Decide("1.0.0", null, floor: "0.7.0"),
        })
        {
            var detail = decision.Refusal!.Detail;
            Assert.DoesNotMatch(@"\d+\.\d+\.\d+", detail);
            Assert.False(string.IsNullOrWhiteSpace(detail));
        }
    }

    [Fact]
    public void A_zero_floor_never_refuses_an_old_record()
    {
        Assert.Equal(GateOutcome.Upgrade, Decide("0.9.0", Row("0.1.0")).Outcome);
        Assert.Equal(Zero, V("0.0.0"));
    }
}
