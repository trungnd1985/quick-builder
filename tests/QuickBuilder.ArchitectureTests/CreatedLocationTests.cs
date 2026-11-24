using System.Text.RegularExpressions;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// A <c>201 Created</c>'s <c>Location</c> carries the base path (#57; 009 FR-017: every
/// address the product generates includes it). ASP.NET Core writes the value as given, so an
/// address that starts with <c>/</c> must start from <c>Request.PathBase</c>.
/// </summary>
public sealed partial class CreatedLocationTests
{
    [Fact]
    public void Every_created_location_starts_from_the_base_path()
    {
        var root = RepositoryRoot();
        var separator = Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => RootedLocation().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(root, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Start a Created location from {Request.PathBase}:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [InlineData("""return Created($"/api/v1/sites/{id}", value);""", true)]
    [InlineData("""return Created("/api/v1/identity/me", value);""", true)]
    [InlineData("""? Created(new Uri($"/api/v1/pages/{id}", UriKind.Relative), value)""", true)]
    [InlineData("""return Created($"{Request.PathBase}/api/v1/sites/{id}", value);""", false)]
    [InlineData("""? Created(new Uri($"{Request.PathBase}/api/v1/pages/{id}", UriKind.Relative), value)""", false)]
    [InlineData("""SchemaChangeEntry.Created(collection, actor, now)""", false)]
    public void The_rule_sees_a_rooted_location(string line, bool matches)
    {
        // Guards the rule itself: a pattern that matched nothing would pass for the wrong reason.
        Assert.Equal(matches, RootedLocation().IsMatch(line));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickBuilder.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    // Created( or Created(new Uri( followed by a string literal that starts with "/".
    [GeneratedRegex(@"\bCreated\(\s*(?:new\s+Uri\(\s*)?[$@]*""/", RegexOptions.CultureInvariant)]
    private static partial Regex RootedLocation();
}
