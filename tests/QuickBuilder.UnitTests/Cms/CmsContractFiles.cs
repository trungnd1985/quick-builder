using System.Text.Json;

namespace QuickBuilder.UnitTests.Cms;

/// <summary>The collection schema contract's files, read from docs/contracts where they live.</summary>
internal static class CmsContractFiles
{
    public static string ExamplesDirectory { get; } =
        Path.Combine(FindRepositoryRoot(), "docs", "contracts", "collection-schema", "examples");

    public static IEnumerable<string> ValidExamples() =>
        Directory.GetFiles(Path.Combine(ExamplesDirectory, "valid"), "*.json").Order(StringComparer.Ordinal);

    /// <summary>Every invalid example with the code, rule and pointer <c>expected.json</c> gives.</summary>
    public static IEnumerable<(string File, string Code, string Rule, string Pointer)> InvalidExamples()
    {
        var directory = Path.Combine(ExamplesDirectory, "invalid");
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "expected.json")));

        foreach (var entry in expected.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('$')))
        {
            yield return (
                Path.Combine(directory, entry.Name),
                entry.Value.GetProperty("code").GetString()!,
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

        throw new InvalidOperationException("The repository root (QuickBuilder.slnx) was not found.");
    }
}
