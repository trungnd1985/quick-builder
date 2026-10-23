using System.Text.RegularExpressions;

namespace QuickBuilder.Release;

/// <summary>
/// <c>migrations check</c>: a migration that shipped in a release never changes
/// (019 FR-017, research RL-12). Git already holds the shipped bytes.
/// </summary>
internal static partial class MigrationFreeze
{
    internal sealed record Result(IReadOnlyList<string> Problems, string? Notice);

    /// <param name="repositoryRoot">The repository to check.</param>
    /// <param name="since">The latest available release tag; none before the first release.</param>
    public static Result Check(string repositoryRoot, string? since)
    {
        if (!Git.Exists(repositoryRoot, since))
        {
            return new Result([], $"No release '{since}' to compare with: nothing has shipped yet, so every migration may still change.");
        }

        var problems = Git.Changes(repositoryRoot, since!, "src")
            .Where(c => IsMigration(c.Path) && c.Status is 'M' or 'D' or 'R' or 'T')
            .Select(c => c.Status switch
            {
                'D' => $"{c.Path}: deleted, but it shipped in {since}.",
                'R' => $"{c.Path}: renamed to {c.NewPath}, but it shipped in {since}.",
                _ => $"{c.Path}: modified, but it shipped in {since}. Add a new migration instead.",
            })
            .ToList();

        return new Result(problems, null);
    }

    /// <summary>An EF Core migration file (timestamp, name, optionally .Designer); not the model snapshot.</summary>
    public static bool IsMigration(string path) => Migration().IsMatch(path);

    [GeneratedRegex(@"(^|/)(Infrastructure|Platform)/Migrations/\d{14}_[^/]+\.cs$", RegexOptions.CultureInvariant)]
    private static partial Regex Migration();
}
