using System.Reflection;

namespace QuickBuilder.Shared.Infrastructure.Versioning;

/// <summary>
/// What this build is: its release version, the git revision it was built from, and the
/// oldest release it may upgrade a database from (019 research RL-01, FR-001, FR-031).
/// </summary>
/// <remarks>
/// <c>Directory.Build.props</c> holds <c>VersionPrefix</c>, <c>VersionSuffix</c> and
/// <c>QbUpgradeFloor</c>; the release pipeline empties the suffix and passes the revision. The
/// values reach the running process as the entry assembly's informational version
/// (<c>0.2.0+5f549ae…</c>) and an <c>AssemblyMetadata("QbUpgradeFloor")</c> attribute.
/// </remarks>
public sealed class ApplicationVersion
{
    /// <summary>The metadata key the Host project writes the floor under.</summary>
    public const string UpgradeFloorKey = "QbUpgradeFloor";

    /// <summary>The metadata key the Host project writes the documentation address under (026 OD-11).</summary>
    public const string DocsBaseUrlKey = "QbDocsBaseUrl";

    /// <summary>Where the operator documentation is published when a build does not say.</summary>
    public const string DefaultDocsBaseUrl = "https://trungnd1985.github.io/quick-builder/";

    /// <summary>
    /// The published operator documentation, from <c>Directory.Build.props</c> through the entry
    /// assembly; each minor line lives under <c>{DocsBaseUrl}{major}.{minor}/</c>.
    /// </summary>
    public static string DocsBaseUrl { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == DocsBaseUrlKey)?.Value is { Length: > 0 } url
            ? url
            : DefaultDocsBaseUrl;

    private const int RevisionLength = 12;

    private ApplicationVersion(SemanticVersion version, string? revision, SemanticVersion upgradeFloor)
    {
        Version = version;
        Revision = revision;
        UpgradeFloor = upgradeFloor;
    }

    /// <summary>The release this build is, e.g. <c>0.2.0</c> or <c>0.3.0-dev</c>.</summary>
    public SemanticVersion Version { get; }

    /// <summary>The first 12 characters of the git revision, or null when the build had none.</summary>
    public string? Revision { get; }

    /// <summary>
    /// The oldest release whose database this build upgrades. <c>0.0.0</c> throughout 0.x
    /// (clarification Q5); the last minor of the previous major from 1.0.0 on.
    /// </summary>
    public SemanticVersion UpgradeFloor { get; }

    /// <summary><c>0.2.0 (5f549ae1c2d3)</c>, or just the version without a revision.</summary>
    public string Describe() => Revision is null ? Version.ToString() : $"{Version} ({Revision})";

    /// <summary>Reads the version of the assembly that started the process.</summary>
    public static ApplicationVersion FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException(
                $"{assembly.GetName().Name} carries no informational version, so this build cannot say which release it is.");

        var floor = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == UpgradeFloorKey)?.Value;

        return Create(informational, floor);
    }

    /// <summary>Builds the version from the raw strings a build produces.</summary>
    /// <exception cref="FormatException">When either string is not a semantic version.</exception>
    public static ApplicationVersion Create(string informationalVersion, string? upgradeFloor)
    {
        ArgumentNullException.ThrowIfNull(informationalVersion);

        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        var revision = plus >= 0 ? informationalVersion[(plus + 1)..] : null;
        if (string.IsNullOrEmpty(revision))
        {
            revision = null;
        }
        else if (revision.Length > RevisionLength)
        {
            revision = revision[..RevisionLength];
        }

        var version = SemanticVersion.Parse(informationalVersion);
        var floor = string.IsNullOrEmpty(upgradeFloor) ? SemanticVersion.Zero : SemanticVersion.Parse(upgradeFloor);

        return new ApplicationVersion(version, revision, floor);
    }
}
