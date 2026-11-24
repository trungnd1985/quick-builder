using System.Text.RegularExpressions;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// The admin's root is spelled once, in <c>AdminRoutes.Root</c> (#53). A link built from its
/// own <c>"/admin"</c> literal goes on working only while the host happens to serve the admin
/// there; #50 was exactly such a link, built from a setting instead of the host's route.
/// </summary>
public sealed partial class AdminRouteSpellingTests
{
    private static readonly string Home = Path.Combine("src", "Shared", "QuickBuilder.Shared.Kernel", "Routing", "AdminRoutes.cs");

    [Fact]
    public void No_source_outside_AdminRoutes_spells_the_admin_root()
    {
        var root = RepositoryRoot();
        var separator = Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !f.EndsWith(Home, StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => AdminLiteral().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(root, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Use AdminRoutes.Root instead of spelling the admin's root:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [InlineData("""private const string AdminRoot = "/admin";""", true)]
    [InlineData("""var part = $"/admin/set-password#token={token}";""", true)]
    [InlineData("""var part = @"/admin/sites";""", true)]
    [InlineData("""var part = $"{AdminRoutes.Root}/set-password";""", false)]
    [InlineData("""/// The admin is served at <c>/admin</c>.""", false)]
    [InlineData("""var other = "/administrators";""", false)]
    public void The_rule_sees_the_literal(string line, bool matches)
    {
        // Guards the rule itself: a pattern that matched nothing would pass for the wrong reason.
        Assert.Equal(matches, AdminLiteral().IsMatch(line));
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

    // A string literal (plain, interpolated or verbatim) that starts with the admin's root as a
    // whole segment: "/admin", "/admin/...", "/admin?...", "/admin#...".
    [GeneratedRegex(@"[$@]*""/admin(?=[""/?#{])", RegexOptions.CultureInvariant)]
    private static partial Regex AdminLiteral();
}
