extern alias releasetool;

using releasetool::QuickBuilder.Release;
using SemanticVersion = releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion;

namespace QuickBuilder.UnitTests.Release;

/// <summary>
/// <c>qb-release upgrade-sources</c>: which earlier releases a release's upgrade gate starts from
/// (019 research RL-09, FR-008, clarifications Q3 and Q5).
/// </summary>
public sealed class UpgradeSourcesTests
{
    private static IReadOnlyList<string> Sources(string version, string available) =>
        UpgradeSources.Compute(
            SemanticVersion.Parse(version),
            [.. available.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(SemanticVersion.Parse)]);

    [Fact]
    public void The_first_release_starts_from_the_baseline() =>
        Assert.Equal(["baseline"], Sources("0.1.0", ""));

    [Fact]
    public void A_0x_release_starts_from_the_previous_one_and_from_0_1_0() =>
        Assert.Equal(["0.3.1", "0.1.0"], Sources("0.4.0", "0.1.0 0.2.0 0.3.1"));

    [Fact]
    public void The_first_major_starts_from_the_last_minor_of_0x_once() =>
        Assert.Equal(["0.7.2"], Sources("1.0.0", "0.1.0 0.2.0 0.7.0 0.7.2"));

    [Fact]
    public void A_minor_starts_from_the_previous_release_and_from_x_0_0() =>
        Assert.Equal(["1.2.0", "1.0.0"], Sources("1.3.0", "1.0.0 1.2.0"));

    [Fact]
    public void A_new_major_starts_from_the_previous_release_which_is_the_last_minor_before_it() =>
        Assert.Equal(["1.4.2"], Sources("2.0.0", "0.1.0 1.0.0 1.4.0 1.4.2"));

    [Fact]
    public void Release_candidates_are_never_sources() =>
        Assert.Equal(["0.1.0"], Sources("0.2.0", "0.1.0 0.2.0-rc.1"));

    [Fact]
    public void A_patch_of_an_older_minor_starts_from_the_releases_below_it() =>
        Assert.Equal(["0.2.0", "0.1.0"], Sources("0.2.1", "0.1.0 0.2.0 0.3.0"));
}
