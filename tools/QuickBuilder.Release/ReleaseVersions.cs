using System.Text.RegularExpressions;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Release;

/// <summary>What kind of release a tag is.</summary>
internal enum ReleaseKind
{
    Major,
    Minor,
    Patch,
    PreRelease,
}

/// <summary>
/// <c>version check</c>: the tag must be the release Directory.Build.props names next, and above
/// what is already available (019 research RL-01, contracts/release-pipeline.md §5).
/// </summary>
internal static partial class ReleaseVersions
{
    internal sealed record Checked(SemanticVersion Version, ReleaseKind Kind, SemanticVersion? Previous);

    /// <param name="tag">The pushed tag, <c>vX.Y.Z</c> or <c>vX.Y.Z-rc.N</c>.</param>
    /// <param name="available">Every published release (pre-releases are ignored).</param>
    /// <param name="props">The version properties of the commit being released.</param>
    public static Checked Check(string tag, IReadOnlyList<SemanticVersion> available, BuildProps props)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(props);

        if (!tag.StartsWith('v') || !SemanticVersion.TryParse(tag[1..], out var version))
        {
            throw new ReleaseRuleException($"Tag '{tag}' is not vX.Y.Z or vX.Y.Z-rc.N.");
        }

        if (version.PreRelease is { } label && !ReleaseCandidate().IsMatch(label))
        {
            throw new ReleaseRuleException($"Tag '{tag}': the only pre-release label is rc.N.");
        }

        var core = new SemanticVersion(version.Major, version.Minor, version.Patch);
        if (core != props.VersionPrefix)
        {
            throw new ReleaseRuleException(
                $"Tag '{tag}' is not the next release: Directory.Build.props has VersionPrefix {props.VersionPrefix}.");
        }

        var releases = available.Where(v => !v.IsPreRelease).ToList();

        // A new minor or major goes above every release; a patch above its own minor's.
        var above = releases.Where(r => r >= core && (core.Patch == 0 || r.CompareMajorMinor(core) == 0)).ToList();
        if (above.Count > 0)
        {
            throw new ReleaseRuleException($"Tag '{tag}': {above.Max()} is already released.");
        }

        var previous = releases.Where(r => r < core).Select(r => (SemanticVersion?)r).Max();
        var kind = version.IsPreRelease ? ReleaseKind.PreRelease
            : previous is null ? ReleaseKind.Minor
            : core.Major > previous.Value.Major && core.Major > 0 ? ReleaseKind.Major
            : core.Patch > 0 ? ReleaseKind.Patch
            : ReleaseKind.Minor;

        if (core is { Major: > 0, Minor: 0, Patch: 0 })
        {
            CheckFloor(core, releases, props.UpgradeFloor);
        }

        return new Checked(version, kind, previous);
    }

    /// <summary>
    /// A new major upgrades from the last minor of the previous major (FR-031), so its build must
    /// say so: <c>QbUpgradeFloor</c> is that minor's x.y.0.
    /// </summary>
    private static void CheckFloor(SemanticVersion major, IReadOnlyList<SemanticVersion> releases, SemanticVersion floor)
    {
        if (releases.Where(r => r.Major == major.Major - 1).Select(r => (SemanticVersion?)r).Max() is not { } last)
        {
            return;
        }

        var expected = new SemanticVersion(last.Major, last.Minor, 0);
        if (floor != expected)
        {
            throw new ReleaseRuleException(
                $"{major} starts a major: Directory.Build.props must set QbUpgradeFloor to {expected} (the last minor of {last.Major}.x), not {floor}.");
        }
    }

    [GeneratedRegex(@"^rc\.[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseCandidate();
}
