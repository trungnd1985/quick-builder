using System.Xml.Linq;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Release;

/// <summary>
/// The version properties in <c>Directory.Build.props</c>: the next release
/// (<c>VersionPrefix</c>) and the oldest release a build upgrades from
/// (<c>QbUpgradeFloor</c>) (019 research RL-01).
/// </summary>
internal sealed record BuildProps(SemanticVersion VersionPrefix, SemanticVersion UpgradeFloor)
{
    internal const string FileName = "Directory.Build.props";

    /// <summary>Where the operator documentation is published when the props do not say (026 OD-11).</summary>
    internal const string DefaultDocsBaseUrl = "https://trungnd1985.github.io/quick-builder/";

    /// <summary><c>QbDocsBaseUrl</c>: each minor line's documentation lives under it.</summary>
    public string DocsBaseUrl { get; init; } = DefaultDocsBaseUrl;

    internal static BuildProps Read(string repositoryRoot)
    {
        var document = Load(repositoryRoot);
        return new BuildProps(
            SemanticVersion.Parse(Value(document, "VersionPrefix")),
            SemanticVersion.Parse(Value(document, "QbUpgradeFloor")))
        {
            DocsBaseUrl = document.Descendants("QbDocsBaseUrl").SingleOrDefault()?.Value.Trim() is { Length: > 0 } url ? url : DefaultDocsBaseUrl,
        };
    }

    /// <summary>Rewrites <c>VersionPrefix</c> in place, keeping every other byte of the file.</summary>
    internal static void WriteVersionPrefix(string repositoryRoot, SemanticVersion prefix)
    {
        var path = Path.Combine(repositoryRoot, FileName);
        var text = File.ReadAllText(path);
        var current = Value(XDocument.Parse(text), "VersionPrefix");

        var updated = text.Replace(
            $"<VersionPrefix>{current}</VersionPrefix>",
            $"<VersionPrefix>{prefix}</VersionPrefix>",
            StringComparison.Ordinal);

        File.WriteAllText(path, updated);
    }

    private static XDocument Load(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, FileName);
        if (!File.Exists(path))
        {
            throw new ReleaseRuleException($"{FileName} not found at {repositoryRoot}.");
        }

        return XDocument.Load(path);
    }

    private static string Value(XDocument document, string property) =>
        document.Descendants(property).SingleOrDefault()?.Value.Trim()
        ?? throw new ReleaseRuleException($"{FileName} has no <{property}>.");
}
