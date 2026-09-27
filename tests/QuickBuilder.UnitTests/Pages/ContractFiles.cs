using System.Text.Json;

namespace QuickBuilder.UnitTests.Pages;

/// <summary>The page schema contract's files, read from docs/contracts where they live.</summary>
internal static class ContractFiles
{
    public static string Root { get; } = FindRepositoryRoot();

    public static string PageSchemaDirectory => Path.Combine(Root, "docs", "contracts", "page-schema");

    public static string ExamplesDirectory => Path.Combine(PageSchemaDirectory, "examples");

    public static IEnumerable<string> ValidExamples() =>
        Directory.GetFiles(Path.Combine(ExamplesDirectory, "valid"), "*.json").Order(StringComparer.Ordinal);

    /// <summary>Every invalid example with the rule and pointer <c>expected.json</c> says it fails with.</summary>
    public static IEnumerable<(string File, string Rule, string Pointer)> InvalidExamples()
    {
        var directory = Path.Combine(ExamplesDirectory, "invalid");
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "expected.json")));

        foreach (var entry in expected.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('$')))
        {
            yield return (
                Path.Combine(directory, entry.Name),
                entry.Value.GetProperty("rule").GetString()!,
                entry.Value.GetProperty("pointer").GetString()!);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (QuickBuilder.slnx) was not found above the test binaries.");
    }
}
