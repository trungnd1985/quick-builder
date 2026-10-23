using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.UnitTests.Versioning;

/// <summary>
/// One version number, ordered the way SemVer 2.0 orders it, read from the build (019 research
/// RL-01, data-model §1).
/// </summary>
public sealed class ApplicationVersionTests
{
    [Theory]
    [InlineData("0.1.0", 0, 1, 0, null)]
    [InlineData("0.3.0-dev", 0, 3, 0, "dev")]
    [InlineData("1.2.3-rc.4", 1, 2, 3, "rc.4")]
    [InlineData("0.2.0+abc123", 0, 2, 0, null)]
    [InlineData("10.20.30-rc.1+5f549ae", 10, 20, 30, "rc.1")]
    public void Parses_release_pre_release_and_build_metadata(string text, int major, int minor, int patch, string? preRelease)
    {
        var version = SemanticVersion.Parse(text);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(preRelease, version.PreRelease);
        Assert.Equal(preRelease is not null, version.IsPreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.2.-3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("1.2.3-rc.01")]
    [InlineData("1.2.3-rc_1")]
    [InlineData("1.2.3+")]
    public void Refuses_malformed_versions(string text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
        Assert.Throws<FormatException>(() => SemanticVersion.Parse(text));
    }

    [Fact]
    public void Orders_as_SemVer_section_11_does()
    {
        string[] ascending =
        [
            "0.0.0",
            "0.2.0-dev",
            "0.2.0-rc.1",
            "0.2.0-rc.2",
            "0.2.0-rc.10",
            "0.2.0",
            "0.2.1",
            "0.3.0-dev",
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0",
        ];

        for (var i = 1; i < ascending.Length; i++)
        {
            var lower = SemanticVersion.Parse(ascending[i - 1]);
            var higher = SemanticVersion.Parse(ascending[i]);
            Assert.True(lower < higher, $"{lower} should sort below {higher}");
            Assert.True(higher > lower);
            Assert.True(lower.CompareTo(higher) < 0);
        }
    }

    [Fact]
    public void Build_metadata_does_not_affect_equality_or_order()
    {
        Assert.Equal(SemanticVersion.Parse("0.2.0"), SemanticVersion.Parse("0.2.0+abc"));
        Assert.Equal(0, SemanticVersion.Parse("0.2.0+abc").CompareTo(SemanticVersion.Parse("0.2.0+def")));
    }

    [Theory]
    [InlineData("1.4.2", "1.4.3", 0)]
    [InlineData("0.3.0-dev", "0.3.0", 0)]
    [InlineData("0.3.0-dev", "0.2.9", 1)]
    [InlineData("1.3.9", "1.4.0", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    public void Major_minor_comparison_ignores_patch_and_pre_release(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(SemanticVersion.Parse(left).CompareMajorMinor(SemanticVersion.Parse(right))));
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.3.0-dev")]
    [InlineData("1.2.3-rc.4")]
    public void Round_trips_through_text(string text)
    {
        Assert.Equal(text, SemanticVersion.Parse(text).ToString());
    }

    [Fact]
    public void Reads_version_revision_and_floor_from_the_build()
    {
        var version = ApplicationVersion.Create("0.2.0-dev+5f549ae1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7", "0.1.0");

        Assert.Equal(SemanticVersion.Parse("0.2.0-dev"), version.Version);
        Assert.Equal("5f549ae1c2d3", version.Revision);
        Assert.Equal(SemanticVersion.Parse("0.1.0"), version.UpgradeFloor);
        Assert.Equal("0.2.0-dev (5f549ae1c2d3)", version.Describe());
    }

    [Fact]
    public void A_build_without_revision_or_floor_has_none_and_a_zero_floor()
    {
        var version = ApplicationVersion.Create("0.1.0", upgradeFloor: null);

        Assert.Null(version.Revision);
        Assert.Equal(SemanticVersion.Zero, version.UpgradeFloor);
        Assert.Equal("0.1.0", version.Describe());
    }

    [Fact]
    public void An_unreadable_build_version_fails_loudly()
    {
        // A build that cannot say what it is must not guess: the startup check depends on it.
        Assert.Throws<FormatException>(() => ApplicationVersion.Create("1.0.0.0", "0.0.0"));
        Assert.Throws<FormatException>(() => ApplicationVersion.Create("0.1.0", "zero"));
    }
}
