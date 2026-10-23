namespace QuickBuilder.Release;

/// <summary>
/// <c>fragment check</c>: a pull request that changes something operators must hear about adds
/// a release-note fragment, and every fragment it adds parses (019 contracts/release-notes.md §4).
/// </summary>
internal static class FragmentCheck
{
    /// <param name="repositoryRoot">The repository, at the pull request's head.</param>
    /// <param name="baseReference">The branch the pull request goes into.</param>
    public static IReadOnlyList<string> Check(string repositoryRoot, string baseReference)
    {
        var mergeBase = Git.Run(repositoryRoot, "merge-base", baseReference, "HEAD").Trim();
        var changes = Git.Changes(repositoryRoot, mergeBase);

        var needs = changes
            .Where(c => NeedsFragment(c.Status, c.Path))
            .Select(c => c.Path)
            .ToList();

        var problems = new List<string>();
        var fragments = changes
            .Where(c => c.Status is 'A' or 'M' && c.Path.StartsWith(NotesAssembler.Unreleased + "/", StringComparison.Ordinal) && c.Path.EndsWith(".md", StringComparison.Ordinal))
            .Select(c => c.Path)
            .ToList();

        foreach (var fragment in fragments)
        {
            try
            {
                Fragment.Parse(fragment, File.ReadAllText(Path.Combine(repositoryRoot, fragment)));
            }
            catch (ReleaseRuleException ex)
            {
                problems.Add(ex.Message);
            }
        }

        if (needs.Count > 0 && fragments.Count == 0)
        {
            problems.AddRange(needs.Select(p =>
                $"{p}: operators must hear about this change; add a fragment to {NotesAssembler.Unreleased}/ (contracts/release-notes.md §2)."));
        }

        return problems;
    }

    private static bool NeedsFragment(char status, string path) =>
        (status == 'A' && MigrationFreeze.IsMigration(path))
        || path == ConfigurationKeys.Guide
        || path == "docker-compose.yml"
        || (path.StartsWith("docs/contracts/", StringComparison.Ordinal) && !path.Contains("/examples/", StringComparison.Ordinal));
}
