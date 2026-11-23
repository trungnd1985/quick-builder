namespace QuickBuilder.Docs.Settings;

/// <summary>
/// <c>settings generate</c>, <c>settings check</c> and <c>settings keys</c> (026 contracts/
/// qb-docs-cli.md; FR-009, FR-015, FR-016).
/// </summary>
internal static class SettingsCommands
{
    public static int Generate(string repositoryRoot, string? outPath, TextWriter output, TextWriter error)
    {
        var catalog = CatalogBuilder.BuildForProduct(repositoryRoot);
        if (Report(catalog.Problems, error))
        {
            return Program.Failed;
        }

        var target = Path.Combine(repositoryRoot, outPath ?? ReferenceWriter.ReferencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, ReferenceWriter.Write(catalog, ReferenceWriter.NotesFrom(repositoryRoot)));
        output.WriteLine($"Wrote {Path.GetRelativePath(repositoryRoot, target).Replace('\\', '/')} ({catalog.Settings.Count()} settings).");
        return Program.Pass;
    }

    public static int Check(string repositoryRoot, TextWriter output, TextWriter error)
    {
        var catalog = CatalogBuilder.BuildForProduct(repositoryRoot);
        var problems = new List<string>(catalog.Problems);

        var expected = ReferenceWriter.Write(catalog, ReferenceWriter.NotesFrom(repositoryRoot));
        var path = Path.Combine(repositoryRoot, ReferenceWriter.ReferencePath);
        var actual = File.Exists(path) ? File.ReadAllText(path) : null;
        if (Difference(expected, actual) is { } difference)
        {
            problems.Add($"{ReferenceWriter.ReferencePath}: {difference} — Run: {Program.GenerateCommand}");
        }

        ReviewList(catalog, output);
        if (Report(problems, error))
        {
            return Program.Failed;
        }

        output.WriteLine($"The configuration reference is current ({catalog.Settings.Count()} settings).");
        return Program.Pass;
    }

    public static int Keys(string repositoryRoot, TextWriter output, TextWriter error)
    {
        var catalog = CatalogBuilder.BuildForProduct(repositoryRoot);
        if (Report(catalog.Problems, error))
        {
            return Program.Failed;
        }

        foreach (var setting in catalog.Settings)
        {
            output.WriteLine(setting.Key);
        }

        return Program.Pass;
    }

    /// <summary>
    /// Null when the committed text equals the generated one (line endings aside); otherwise
    /// where they first differ, with both lines.
    /// </summary>
    internal static string? Difference(string expected, string? actual)
    {
        if (actual is null)
        {
            return "missing";
        }

        var want = Normalise(expected).Split('\n');
        var have = Normalise(actual).Split('\n');
        for (var i = 0; i < Math.Max(want.Length, have.Length); i++)
        {
            var w = i < want.Length ? want[i] : "(end of file)";
            var h = i < have.Length ? have[i] : "(end of file)";
            if (w != h)
            {
                return $"out of date at line {i + 1}:\n  - committed: {h}\n  + generated: {w}\n ";
            }
        }

        return null;
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void ReviewList(Catalog catalog, TextWriter output)
    {
        output.WriteLine("For review:");
        foreach (var exclusion in catalog.Exclusions)
        {
            output.WriteLine($"  excluded ({exclusion.Kind}) {exclusion.Pattern}: {exclusion.Reason}");
        }

        foreach (var setting in catalog.Settings.Where(s => s.DefaultIsComputed))
        {
            output.WriteLine($"  computed default {setting.Key}: {setting.Default}");
        }

        foreach (var setting in catalog.Settings.Where(s => s.ObservedLazily is not null))
        {
            output.WriteLine($"  not read at startup {setting.Key}: {setting.ObservedLazily}");
        }
    }

    private static bool Report(IReadOnlyList<string> problems, TextWriter error)
    {
        foreach (var problem in problems.Order(StringComparer.Ordinal))
        {
            error.WriteLine(problem);
        }

        return problems.Count > 0;
    }
}
