using System.Text.RegularExpressions;
using QuickBuilder.Docs.Settings;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// Every configuration key the product's source names literally is a documented setting (026
/// FR-015; research OD-03).
/// </summary>
/// <remarks>
/// The boot observation (<c>EveryReadSettingIsDeclared</c>) sees what the configuration binder
/// and the running services read, but not what <c>Program.cs</c> reads before the host is
/// built: a test's extra configuration source is attached only then. This scan covers those
/// direct reads by their literal keys: <c>configuration["A:B"]</c>, <c>GetSection("A:B")</c>,
/// <c>GetConnectionString("X")</c> and <c>GetValue&lt;T&gt;("A:B")</c>. Keys built at run time are
/// the boot observation's to catch.
/// </remarks>
public sealed partial class LiteralConfigurationReadsTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    /// <summary>Every literal key read anywhere under <c>src/</c>, with where it is read.</summary>
    internal static IEnumerable<(string Key, string Where)> LiteralReads(string root)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var where = $"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{i + 1}";
                foreach (Match match in ConnectionString().Matches(lines[i]))
                {
                    yield return ($"ConnectionStrings:{match.Groups["name"].Value}", where);
                }

                foreach (Match match in Indexer().Matches(lines[i]).Concat(Section().Matches(lines[i])).Concat(GetValue().Matches(lines[i])))
                {
                    yield return (match.Groups["key"].Value, where);
                }
            }
        }
    }

    [Fact]
    public void Every_literal_key_the_product_reads_is_documented()
    {
        var root = RepositoryRoot();
        var catalog = CatalogBuilder.BuildForProduct(root);

        var undeclared = LiteralReads(root)
            .Where(read => !catalog.Declares(read.Key)
                           && !catalog.IsParentPath(read.Key)
                           && !catalog.Sections.Any(s => string.Equals(s.Key, read.Key, StringComparison.OrdinalIgnoreCase))
                           && !catalog.Exclusions.Any(e => e.Matches(read.Key)))
            .Select(read => $"{read.Where}: reads '{read.Key}', which is not a documented setting — add [Setting] to its options property, or read it through a documented options class.")
            .ToList();

        Assert.True(undeclared.Count == 0, string.Join('\n', undeclared));
    }

    [Fact]
    public void The_scan_finds_each_kind_of_literal_read()
    {
        var reads = LiteralReads(RepositoryRoot()).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ConnectionStrings:Default", reads);
        Assert.Contains("Hosting:AdminHostNames", reads);
    }

    [GeneratedRegex(@"GetConnectionString\(""(?<name>[^""]+)""\)", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionString();

    [GeneratedRegex(@"[Cc]onfiguration\[""(?<key>[A-Za-z][A-Za-z0-9_]*(?::[A-Za-z0-9_]+)+)""\]", RegexOptions.CultureInvariant)]
    private static partial Regex Indexer();

    [GeneratedRegex(@"GetSection\(""(?<key>[A-Za-z][A-Za-z0-9_]*(?::[A-Za-z0-9_]+)*)""\)", RegexOptions.CultureInvariant)]
    private static partial Regex Section();

    [GeneratedRegex(@"GetValue<[^>]+>\(""(?<key>[A-Za-z][A-Za-z0-9_]*(?::[A-Za-z0-9_]+)+)""", RegexOptions.CultureInvariant)]
    private static partial Regex GetValue();
}
