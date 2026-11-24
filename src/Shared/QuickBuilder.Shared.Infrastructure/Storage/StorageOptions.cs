using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Shared.Infrastructure.Storage;

/// <summary>
/// Which storage provider holds files, and its settings (003 research M-05; 013 MV-18 – MV-20).
/// </summary>
/// <remarks>
/// Bound from the <c>Storage</c> section. Validated at startup by the selected provider's
/// validator: an unusable location or account stops the process before it listens, naming
/// the key, rather than surfacing as a user's failed upload.
/// </remarks>
[SettingsSection(SectionName, Area = "storage", Title = "Storage")]
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public const string LocalProvider = "Local";

    public const string S3Provider = "S3";

    public const string AzureBlobProvider = "AzureBlob";

    /// <summary>The providers this release ships. Compared case-insensitively.</summary>
    public static IReadOnlyList<string> SupportedProviders { get; } = [LocalProvider, S3Provider, AzureBlobProvider];

    /// <summary>The provider to use. <c>Local</c> is the default.</summary>
    [Setting("Where uploaded files live: `Local` (the file system), `S3` (any S3-compatible service) or `AzureBlob`, case-insensitive. Only the selected provider is loaded, and only it makes network calls.", Allowed = "`Local`, `S3`, `AzureBlob`")]
    public string Provider { get; set; } = LocalProvider;

    [SettingsGroup("Local file system", AppliesWhen = "Storage:Provider = Local")]
    public LocalStorageSettings Local { get; set; } = new();

    [SettingsGroup("S3-compatible", AppliesWhen = "Storage:Provider = S3")]
    public S3StorageSettings S3 { get; set; } = new();

    [SettingsGroup("Azure Blob", AppliesWhen = "Storage:Provider = AzureBlob")]
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
    [Setting("The directory that holds uploaded files. It is created if missing and must be writable. In a container, mount persistent storage here; the reference compose file uses the `media-data` volume.", DefaultText = "Image: `/var/lib/quickbuilder/media`. Elsewhere: `{LocalApplicationData}/QuickBuilder/media`", Allowed = "an absolute path that is not, and is not inside, the application's own directory, content root or web root")]
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
    [Setting("The bucket. Required with S3. It must exist; QuickBuilder never creates it.")]
    public string Bucket { get; set; } = string.Empty;

    /// <summary>The region. Required; any value for services that ignore regions.</summary>
    [Setting("The region, e.g. `eu-west-1`. Required with S3; any value (such as `auto`) for services that ignore regions.")]
    public string Region { get; set; } = string.Empty;

    /// <summary>An absolute http(s) URL for MinIO, R2, Ceph and others. Empty means AWS.</summary>
    [Setting("The service address for anything other than AWS, e.g. `https://<account>.r2.cloudflarestorage.com`. Empty means AWS.", Format = "empty, or an absolute `http(s)` URL")]
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Path-style addressing, which most self-hosted services need.</summary>
    [Setting("Address buckets by path rather than by host name; `true` for most self-hosted services (MinIO, Ceph).")]
    public bool ForcePathStyle { get; set; }

    /// <summary>Prepended to every object name, e.g. <c>quickbuilder/</c>.</summary>
    [Setting("Puts every object under a prefix, e.g. `quickbuilder/`, so one bucket or container can serve several installations.", Format = "empty, or lower-case letters, digits and `-` in segments, ending in `/`")]
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>With <see cref="SecretAccessKey"/>, or neither for the ambient credential chain.</summary>
    [Setting("The access key id, with `Storage:S3:SecretAccessKey`, or neither. With neither, the environment's credentials are used: environment variables, web identity or the instance profile.", Secret = false)]
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>A secret: never logged or echoed (013 FR-033).</summary>
    [Setting("The secret access key, with `Storage:S3:AccessKeyId`. It never appears in messages or logs.", Secret = true)]
    public string SecretAccessKey { get; set; } = string.Empty;

    internal bool HasKeys => AccessKeyId.Length > 0 && SecretAccessKey.Length > 0;
}

/// <summary>Settings for the Azure Blob provider (013 contracts/storage-providers.md §4.1).</summary>
public sealed class AzureBlobStorageSettings
{
    /// <summary>The container. Required; it must exist and is never created.</summary>
    [Setting("The container. Required with Azure Blob. It must exist; QuickBuilder never creates it.")]
    public string Container { get; set; } = string.Empty;

    /// <summary>An account-key, SAS or Azurite connection string. A secret. Or use <see cref="AccountUrl"/>.</summary>
    [Setting("An account-key or SAS connection string. Set exactly one of this and `Storage:AzureBlob:AccountUrl`. Neither it nor the key inside it ever appears in messages or logs.", Secret = true)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary><c>https://{account}.blob.core.windows.net</c>, authenticated with the environment's identity.</summary>
    [Setting("The account address, `https://<account>.blob.core.windows.net`, authenticated with the environment's identity (managed identity, workload identity, the `AZURE_*` environment variables or a signed-in Azure CLI) instead of a secret. Set exactly one of this and `Storage:AzureBlob:ConnectionString`.")]
    public string AccountUrl { get; set; } = string.Empty;

    /// <summary>Prepended to every object name, e.g. <c>quickbuilder/</c>.</summary>
    [Setting("Puts every object under a prefix, e.g. `quickbuilder/`, so one bucket or container can serve several installations.", Format = "empty, or lower-case letters, digits and `-` in segments, ending in `/`")]
    public string KeyPrefix { get; set; } = string.Empty;
}
