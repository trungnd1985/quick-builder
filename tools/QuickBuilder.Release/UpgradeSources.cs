using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Release;

/// <summary>
/// <c>upgrade-sources</c>: which earlier releases a release's upgrade gate starts from
/// (019 research RL-09, FR-008).
/// </summary>
internal static class UpgradeSources
{
    /// <summary>The source of the first release, which has no earlier one (research RL-15).</summary>
    public const string Baseline = "baseline";

    /// <returns>The release just before; the first release of this major (0.1.0 throughout 0.x);
    /// and, for a new major, the last minor of the previous one — without duplicates.</returns>
    public static IReadOnlyList<string> Compute(SemanticVersion version, IReadOnlyList<SemanticVersion> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        var core = new SemanticVersion(version.Major, version.Minor, version.Patch);
        var below = available.Where(v => !v.IsPreRelease && v < core).ToList();
        if (below.Count == 0)
        {
            return [Baseline];
        }

        var sources = new List<SemanticVersion> { below.Max() };

        if (below.Where(v => v.Major == core.Major).Select(v => (SemanticVersion?)v).Min() is { } firstOfMajor)
        {
            sources.Add(firstOfMajor);
        }
        else if (below.Where(v => v.Major == core.Major - 1).Select(v => (SemanticVersion?)v).Max() is { } lastOfPrevious)
        {
            sources.Add(lastOfPrevious);
        }

        return [.. sources.Distinct().Select(v => v.ToString())];
    }
}
