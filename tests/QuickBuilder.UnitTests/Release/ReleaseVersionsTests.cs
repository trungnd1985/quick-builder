extern alias releasetool;

using releasetool::QuickBuilder.Release;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// <c>qb-release version check</c>: the tag is the release Directory.Build.props says is next,
/// and what kind of release it is (019 contracts/release-pipeline.md §5, RL-01, FR-001a).
/// </summary>
public sealed class ReleaseVersionsTests
{
    private static ReleaseVersions.Checked Check(string tag, string prefix, string available = "", string floor = "0.0.0") =>
        ReleaseVersions.Check(tag, Versions(available), new BuildProps(V(prefix), V(floor)));

    private static releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion V(string text) =>
        releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion.Parse(text);

    private static IReadOnlyList<releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion> Versions(string list) =>
        [.. list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(V)];

    [Fact]
    public void The_first_release_has_no_previous_and_is_a_minor()
    {
        var result = Check("v0.1.0", "0.1.0");

        Assert.Equal(ReleaseKind.Minor, result.Kind);
        Assert.Null(result.Previous);
        Assert.Equal(V("0.1.0"), result.Version);
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0", "0.1.0 0.1.1", "Minor", "0.1.1")]
    [InlineData("v0.2.1", "0.2.1", "0.1.0 0.2.0", "Patch", "0.2.0")]
    [InlineData("v1.3.0", "1.3.0", "1.0.0 1.2.0", "Minor", "1.2.0")]
    public void Minor_and_patch_releases_are_told_apart(string tag, string prefix, string available, string kind, string previous)
    {
        var result = Check(tag, prefix, available);

        Assert.Equal(Enum.Parse<ReleaseKind>(kind), result.Kind);
        Assert.Equal(V(previous), result.Previous);
    }

    [Fact]
    public void Within_0x_a_new_minor_is_a_minor_not_a_major()
    {
        Assert.Equal(ReleaseKind.Minor, Check("v0.5.0", "0.5.0", "0.1.0 0.4.2").Kind);
    }

    [Fact]
    public void A_release_candidate_is_a_prerelease_of_the_prefix()
    {
        var result = Check("v0.2.0-rc.1", "0.2.0", "0.1.0");

        Assert.Equal(ReleaseKind.PreRelease, result.Kind);
        Assert.Equal(V("0.1.0"), result.Previous);
        Assert.Equal(V("0.2.0-rc.1"), result.Version);
    }

    [Theory]
    [InlineData("v0.2.0", "0.3.0")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("v0.2", "0.2.0")]
    [InlineData("v0.2.0-beta", "0.2.0")]
    public void A_tag_that_is_not_the_prefix_is_refused(string tag, string prefix)
    {
        Assert.Throws<ReleaseRuleException>(() => Check(tag, prefix));
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0", "0.2.0")]
    [InlineData("v0.2.0", "0.2.0", "0.1.0 0.3.0")]
    public void A_tag_at_or_below_an_available_release_is_refused(string tag, string prefix, string available)
    {
        var error = Assert.Throws<ReleaseRuleException>(() => Check(tag, prefix, available));
        Assert.Contains("already", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_major_is_a_major_and_needs_the_floor_at_the_last_minor_of_the_previous_one()
    {
        var result = Check("v1.0.0", "1.0.0", "0.1.0 0.7.0 0.7.2", floor: "0.7.0");
        Assert.Equal(ReleaseKind.Major, result.Kind);

        var wrong = Assert.Throws<ReleaseRuleException>(() => Check("v1.0.0", "1.0.0", "0.1.0 0.7.2", floor: "0.0.0"));
        Assert.Contains("QbUpgradeFloor", wrong.Message, StringComparison.Ordinal);
        Assert.Contains("0.7.0", wrong.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_candidates_in_the_available_list_are_not_previous_releases()
    {
        Assert.Equal(V("0.1.0"), Check("v0.2.0", "0.2.0", "0.1.0 0.2.0-rc.1").Previous);
    }
}
