extern alias releasetool;

using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticVersion = releasetool::QuickBuilder.Shared.Infrastructure.Versioning.SemanticVersion;

namespace QuickBuilder.Docs.Publishing;

/// <summary>One published minor line in <c>/versions.json</c> (026 contracts/docs-publishing.md §4).</summary>
internal sealed record PublishedLine(
    [property: JsonPropertyName("line")] string Line,
    [property: JsonPropertyName("release")] string Release,
    [property: JsonPropertyName("published")] string Published);

/// <summary>
/// <c>publish prepare</c>: puts one release's built site into a <c>gh-pages</c> checkout
/// (026 research OD-08; contracts/docs-publishing.md §3 step 4). It replaces only that minor
/// line's folder and entry, and moves the root redirect only for the highest stable release.
/// </summary>
internal static class VersionsManifest
{
    public const string ManifestFile = "versions.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The minor line of a release, e.g. <c>0.3</c> for 0.3.2.</summary>
    public static string LineOf(SemanticVersion version) => $"{version.Major}.{version.Minor}";

    /// <summary>
    /// Inserts or replaces <paramref name="version"/>'s line, newest line first by number.
    /// </summary>
    /// <exception cref="ArgumentException">A pre-release, which never gets published documentation.</exception>
    public static IReadOnlyList<PublishedLine> Upsert(IReadOnlyList<PublishedLine> lines, SemanticVersion version, DateOnly published)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (version.IsPreRelease)
        {
            throw new ArgumentException($"{version} is a pre-release; its documentation is not published.", nameof(version));
        }

        var line = LineOf(version);
        return
        [
            .. lines.Where(l => l.Line != line)
                .Append(new PublishedLine(line, version.ToString(), published.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
                .OrderByDescending(l => SemanticVersion.Parse(l.Line + ".0")),
        ];
    }

    /// <summary>The root page: a redirect to the latest stable line.</summary>
    public static string RedirectPage(string line) => $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>QuickBuilder operator documentation</title>
        <meta http-equiv="refresh" content="0; url=./{line}/">
        <link rel="canonical" href="./{line}/">
        </head>
        <body>
        <p><a href="./{line}/">QuickBuilder {line} operator documentation</a></p>
        </body>
        </html>

        """;

    public static int Prepare(string site, string build, string versionText, bool highest, DateOnly today, TextWriter output, TextWriter error)
    {
        var version = SemanticVersion.Parse(versionText);
        if (version.IsPreRelease)
        {
            error.WriteLine($"{version}: pre-releases do not publish documentation — skip this step for them.");
            return Program.UsageError;
        }

        if (!Directory.Exists(build) || !File.Exists(Path.Combine(build, "index.html")))
        {
            error.WriteLine($"{build}: no built site (index.html missing) — build docs/operators first.");
            return Program.Failed;
        }

        Directory.CreateDirectory(site);
        var line = LineOf(version);
        var target = Path.Combine(site, line);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        Copy(build, target);

        var manifest = Path.Combine(site, ManifestFile);
        var lines = File.Exists(manifest)
            ? JsonSerializer.Deserialize<List<PublishedLine>>(File.ReadAllText(manifest)) ?? []
            : [];
        File.WriteAllText(manifest, JsonSerializer.Serialize(Upsert(lines, version, today), Json) + "\n");

        var index = Path.Combine(site, "index.html");
        if (highest || !File.Exists(index))
        {
            File.WriteAllText(index, RedirectPage(line));
        }

        var nojekyll = Path.Combine(site, ".nojekyll");
        if (!File.Exists(nojekyll))
        {
            File.WriteAllText(nojekyll, string.Empty);
        }

        output.WriteLine($"Prepared {line}/ from {version}{(highest ? "; the root now opens it" : string.Empty)}.");
        return Program.Pass;
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
