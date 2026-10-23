using System.Text.RegularExpressions;

namespace QuickBuilder.Release;

/// <summary>
/// <c>patch check</c>: a patch adds no migration and raises no content schema version, which is
/// what lets an older patch of the same minor run on its database (019 FR-034a, clarification Q1).
/// </summary>
internal static partial class PatchRules
{
    /// <summary>Where the content schema versions are declared.</summary>
    internal static readonly string[] VersionFiles =
    [
        "src/Modules/Pages/QuickBuilder.Pages.Contracts/PageSchema.cs",
        "src/Modules/Pages/QuickBuilder.Pages.Contracts/MenuSchema.cs",
        "src/Modules/Editor/QuickBuilder.Editor.Contracts/DesignTokens.cs",
        "src/Modules/Cms/QuickBuilder.Cms.Contracts/CollectionSchema.cs",
    ];

    /// <param name="repositoryRoot">The repository to check.</param>
    /// <param name="since">The minor's first release, <c>vX.Y.0</c>.</param>
    public static IReadOnlyList<string> Check(string repositoryRoot, string since)
    {
        var problems = new List<string>();
        var changes = Git.Changes(repositoryRoot, since);

        problems.AddRange(changes
            .Where(c => c.Status == 'A' && MigrationFreeze.IsMigration(c.Path))
            .Select(c => $"{c.Path}: a patch release cannot add a migration; it waits for the next minor."));

        problems.AddRange(changes
            .Where(c => c.Status == 'A' && SchemaFile().IsMatch(c.Path))
            .Select(c => $"{c.Path}: a patch release cannot add a content schema version."));

        foreach (var file in VersionFiles.Where(f => changes.Any(c => c.Path == f)))
        {
            var diff = Git.Run(repositoryRoot, "diff", "-U0", since, "--", file);
            if (diff.Split('\n').Any(ChangesAVersion))
            {
                problems.Add($"{file}: a patch release cannot change a schema version constant.");
            }
        }

        return problems;
    }

    private static bool ChangesAVersion(string line) =>
        (line.StartsWith('+') || line.StartsWith('-'))
        && !line.StartsWith("+++", StringComparison.Ordinal)
        && !line.StartsWith("---", StringComparison.Ordinal)
        && VersionConstant().IsMatch(line);

    [GeneratedRegex(@"^docs/contracts/.+/v\d+\.schema\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaFile();

    [GeneratedRegex(@"Current(Binding)?Version\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex VersionConstant();
}
