using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Release;

/// <summary>
/// <c>archive</c>: after a release is approved, its fragments move to
/// <c>docs/releases/&lt;version&gt;/</c> and <c>VersionPrefix</c> moves to the next minor
/// (019 contracts/release-pipeline.md §7).
/// </summary>
internal static class ReleaseArchive
{
    private const string Keep = ".gitkeep";

    public static void Archive(string repositoryRoot, SemanticVersion version)
    {
        var unreleased = Path.Combine(repositoryRoot, NotesAssembler.Unreleased);
        var target = Path.Combine(repositoryRoot, "docs", "releases", version.ToString());
        Directory.CreateDirectory(target);

        if (Directory.Exists(unreleased))
        {
            foreach (var file in Directory.EnumerateFiles(unreleased).Where(f => Path.GetFileName(f) != Keep))
            {
                File.Move(file, Path.Combine(target, Path.GetFileName(file)));
            }
        }

        BuildProps.WriteVersionPrefix(repositoryRoot, new SemanticVersion(version.Major, version.Minor + 1, 0));
    }
}
