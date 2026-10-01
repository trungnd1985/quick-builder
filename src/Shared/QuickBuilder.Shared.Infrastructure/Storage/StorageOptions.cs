namespace QuickBuilder.Shared.Infrastructure.Storage;

/// <summary>
/// Which storage provider holds files, and its settings (003 research M-05; 013 MV-18 – MV-20).
/// </summary>
/// <remarks>
/// Bound from the <c>Storage</c> section. Validated at startup by the selected provider's
/// validator: an unusable location or account stops the process before it listens, naming
/// the key, rather than surfacing as a user's failed upload.
/// </remarks>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string LocalProvider = "Local";

    public const string S3Provider = "S3";

    public const string AzureBlobProvider = "AzureBlob";

    /// <summary>The providers this release ships. Compared case-insensitively.</summary>
    public static IReadOnlyList<string> SupportedProviders { get; } = [LocalProvider, S3Provider, AzureBlobProvider];

    /// <summary>The provider to use. <c>Local</c> is the default.</summary>
    public string Provider { get; set; } = LocalProvider;

    public LocalStorageSettings Local { get; set; } = new();

    public S3StorageSettings S3 { get; set; } = new();

    public AzureBlobStorageSettings AzureBlob { get; set; } = new();

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

    internal bool IsLocal => Is(LocalProvider);

    internal bool IsS3 => Is(S3Provider);

    internal bool IsAzureBlob => Is(AzureBlobProvider);

    private bool Is(string provider) => string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase);
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

/// <summary>Settings for the S3-compatible provider (013 contracts/storage-providers.md §3.1).</summary>
public sealed class S3StorageSettings
{
    /// <summary>The bucket. Required; it must exist and is never created.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>The region. Required; any value for services that ignore regions.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>An absolute http(s) URL for MinIO, R2, Ceph and others. Empty means AWS.</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Path-style addressing, which most self-hosted services need.</summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>Prepended to every object name, e.g. <c>quickbuilder/</c>.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>With <see cref="SecretAccessKey"/>, or neither for the ambient credential chain.</summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>A secret: never logged or echoed (013 FR-033).</summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    internal bool HasKeys => AccessKeyId.Length > 0 && SecretAccessKey.Length > 0;
}

/// <summary>Settings for the Azure Blob provider (013 contracts/storage-providers.md §4.1).</summary>
public sealed class AzureBlobStorageSettings
{
    /// <summary>The container. Required; it must exist and is never created.</summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>An account-key, SAS or Azurite connection string. A secret. Or use <see cref="AccountUrl"/>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary><c>https://{account}.blob.core.windows.net</c>, authenticated with the environment's identity.</summary>
    public string AccountUrl { get; set; } = string.Empty;

    /// <summary>Prepended to every object name, e.g. <c>quickbuilder/</c>.</summary>
    public string KeyPrefix { get; set; } = string.Empty;
}
