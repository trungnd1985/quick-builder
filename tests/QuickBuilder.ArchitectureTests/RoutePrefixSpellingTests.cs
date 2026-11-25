using System.Text.RegularExpressions;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Each reserved route prefix is spelled once, in <c>Shared.Kernel/Routing</c> (#53, #58, #61):
/// <c>/admin</c>, <c>/api</c>, <c>/health</c>, <c>/_media</c>, <c>/_assets</c>, <c>/_forms</c>
/// and <c>/_preview</c>.
/// </summary>
/// <remarks>
/// A route, a link or a reservation built from its own literal agrees with the others only
/// while nobody moves one of them: #50 was a link that did not follow the admin's route, and
/// the host's reserved prefixes decide which page slugs are refused and which paths skip site
/// resolution. Route templates without a leading slash (<c>[Route("api/v1/…")]</c>) are not
/// prefixes in this sense and are not checked.
/// <para>
/// A public prefix's bare segment (<c>"_forms"</c>) is checked too (#61): segment lists are
/// where a prefix was last left behind, so a redirect from under <c>/_forms</c> was accepted
/// and never fired. The administrative segments (<c>"api"</c>, <c>"admin"</c>,
/// <c>"health"</c>) are ordinary words with other meanings, so they are left to the coverage
/// tests that compare those lists with the host's prefixes.
/// </para>
/// </remarks>
public sealed partial class RoutePrefixSpellingTests
{
    private static readonly string Home = Path.Combine("src", "Shared", "QuickBuilder.Shared.Kernel", "Routing") + Path.DirectorySeparatorChar;

    [Fact]
    public void No_source_outside_the_kernel_spells_a_reserved_prefix()
    {
        var root = RepositoryRoot();
        var separator = Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                && !Path.GetRelativePath(root, f).StartsWith(Home, StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => PrefixLiteral().IsMatch(l.Text))
            .Select(l => $"{Path.GetRelativePath(root, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Use the constants in QuickBuilder.Shared.Kernel.Routing instead of spelling a reserved prefix:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [InlineData("""private const string AdminRoot = "/admin";""", true)]
    [InlineData("""var part = $"/admin/set-password#token={token}";""", true)]
    [InlineData("""var part = @"/admin/sites";""", true)]
    [InlineData("""private static readonly PathString Api = new("/api");""", true)]
    [InlineData("""[HttpGet("/_media/{**file}")]""", true)]
    [InlineData("""path.StartsWithSegments("/_assets", StringComparison.OrdinalIgnoreCase)""", true)]
    [InlineData("""[HttpPost("/_forms/{formId}")]""", true)]
    [InlineData("""$"/_preview/l/{secret}",""", true)]
    [InlineData("""if (context.Request.Path == "/health/live") return;""", true)]
    [InlineData("""var part = $"{AdminRoutes.Root}/set-password";""", false)]
    [InlineData("""[HttpGet(PublicRoutes.Media + "/{**file}")]""", false)]
    [InlineData("""/// The admin is served at <c>/admin</c>.""", false)]
    [InlineData("""[Route("api/v1/sites")]""", false)]
    [InlineData("""var other = "/administrators";""", false)]
    [InlineData("""var other = "/_mediakit";""", false)]
    [InlineData("""|| first is "_media" or "_assets" or "robots.txt" or other""", true)]
    [InlineData("""string[] segments = ["api", "admin", "health", "_preview"];""", true)]
    [InlineData("""var name = "_forms";""", true)]
    [InlineData("""var other = "_mediakit";""", false)]
    [InlineData("""string[] words = ["api", "admin", "health"];""", false)]
    public void The_rule_sees_the_literal(string line, bool matches)
    {
        // Guards the rule itself: a pattern that matched nothing would pass for the wrong reason.
        Assert.Equal(matches, PrefixLiteral().IsMatch(line));
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

    // A string literal (plain, interpolated or verbatim) that starts with a reserved prefix as a
    // whole segment: "/api", "/api/...", "/api?...", "/api#..."; or a public prefix's bare
    // segment: "_forms", "_forms/...".
    [GeneratedRegex(@"[$@]*""(?:/(?:admin|api|health|_media|_assets|_forms|_preview)(?=[""/?#{])|_(?:media|assets|forms|preview)(?=[""/]))", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixLiteral();
}
