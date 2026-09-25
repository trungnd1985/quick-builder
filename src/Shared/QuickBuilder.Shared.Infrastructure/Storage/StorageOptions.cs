namespace QuickBuilder.Shared.Infrastructure.Storage;

/// <summary>
/// Which storage provider holds files, and its settings (003 research M-05).
/// </summary>
/// <remarks>
/// Bound from the <c>Storage</c> section. Validated at startup by
/// <see cref="Local.LocalStorageOptionsValidator"/>: an unusable location stops the process
/// before it listens, naming the key, rather than surfacing as a user's failed upload.
/// </remarks>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>The providers this release ships. Compared case-insensitively.</summary>
    public static IReadOnlyList<string> SupportedProviders { get; } = ["Local"];

    /// <summary>The provider to use. <c>Local</c> is the default and the only one so far.</summary>
    public string Provider { get; set; } = "Local";

    public LocalStorageSettings Local { get; set; } = new();

    /// <summary>
    /// Directories the local provider must never be placed in or under: the application,
    /// content and web roots.
    /// </summary>
    /// <remarks>
    /// Filled in by the host through <c>AddFileStorage</c> and never bound from configuration
    /// (internal, so the binder ignores it). Having the host supply them keeps this project
    /// free of ASP.NET Core (003 research M-05).
    /// </remarks>
    internal List<string> ForbiddenRoots { get; } = [];

    internal bool IsLocal => string.Equals(Provider, "Local", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Settings for the local file-system provider.</summary>
public sealed class LocalStorageSettings
{
    /// <summary>
    /// Absolute directory that holds stored files. Empty means
    /// <see cref="DefaultPath"/>; the container image sets <c>/var/lib/quickbuilder/media</c>.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The per-user default used outside containers (003 research M-06).</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "QuickBuilder",
        "media");
}
