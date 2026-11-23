extern alias releasetool;

using System.Text.RegularExpressions;
using QuickBuilder.Docs.Settings;
using static QuickBuilder.UnitTests.Docs.CatalogFixtures;
using ConfigurationKeys = releasetool::QuickBuilder.Release.ConfigurationKeys;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>
/// The generated reference: its shape, that the committed file is current, and that no key
/// changed spelling since v0.1.0 (026 T029 – T031; FR-009, FR-010, FR-015, FR-017; SC-002).
/// </summary>
public sealed partial class SettingsReferenceTests
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

    [Fact]
    public void The_reference_has_six_columns_and_rows_the_release_tool_parses()
    {
        var text = ReferenceWriter.Write(Build(typeof(Good)), area => area == "database" ? "A note." : null);

        Assert.StartsWith(ReferenceWriter.GeneratedComment, text, StringComparison.Ordinal);
        Assert.Contains("| Key | Environment variable | Type | Default | Allowed values | Description |", text, StringComparison.Ordinal);
        Assert.Contains("## Database\n\nA note.\n", text, StringComparison.Ordinal);
        Assert.Contains("| `Good:Count` | `Good__Count` | whole number | `3` | `1` – `10` | <a id=\"good-count\"></a>A count. |", text, StringComparison.Ordinal);
        Assert.Contains("<a id=\"good-connectionstring\"></a>**Required.** The connection. **Secret.**", text, StringComparison.Ordinal);
        Assert.Contains("#### Remote\n\nApplies when `Good:Choice` = `Fast`.\n", text, StringComparison.Ordinal);
        Assert.Equal(10, KeyRow().Count(text));
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        Assert.Equal(
            ReferenceWriter.Write(Build(typeof(Good)), _ => null),
            ReferenceWriter.Write(Build(typeof(Good)), _ => null));
    }

    [Fact]
    public void The_committed_reference_is_current()
    {
        var root = RepositoryRoot();
        var catalog = CatalogBuilder.BuildForProduct(root);

        Assert.Empty(catalog.Problems);
        var expected = ReferenceWriter.Write(catalog, ReferenceWriter.NotesFrom(root));
        var committed = File.ReadAllText(Path.Combine(root, ReferenceWriter.ReferencePath));
        var difference = SettingsCommands.Difference(expected, committed);

        Assert.True(difference is null, $"{ReferenceWriter.ReferencePath} {difference}\nRun: {QuickBuilder.Docs.Program.GenerateCommand}");
    }

    [Fact]
    public void Every_key_documented_in_v0_1_0_keeps_its_spelling()
    {
        var root = RepositoryRoot();
        if (!GitHasTag(root, "v0.1.0"))
        {
            return; // A shallow clone without tags: the release workflow runs this with full history.
        }

        var before = ConfigurationKeys.At(root, "v0.1.0");
        var now = CatalogBuilder.BuildForProduct(root).Settings.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(before.Except(now));
    }

    private static bool GitHasTag(string root, string tag)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", ["rev-parse", "-q", "--verify", $"refs/tags/{tag}"])
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    [GeneratedRegex(@"^\| `[A-Za-z][^`]*` \|", RegexOptions.Multiline)]
    private static partial Regex KeyRow();
}
