using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Host.Configuration;

/// <summary>
/// How the application sits behind a reverse proxy (FR-012, D-12).
/// </summary>
[SettingsSection(SectionName, Area = "hosting", Title = "Hosting and reverse proxies")]
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>
    /// Base path when the app is served under a sub-path, e.g. <c>/cms</c>. Empty means
    /// the root.
    /// </summary>
    [RegularExpression("^(/[A-Za-z0-9._~-]+)*$", ErrorMessage = "Hosting:BasePath must be empty or start with '/'.")]
    [Setting("Serve the installation under a sub-path, e.g. `/cms`. Empty serves it at the root.",
        Format = "empty, or a path starting with `/`")]
    public string BasePath { get; set; } = string.Empty;

    /// <summary>
    /// Proxy addresses whose forwarded headers are trusted.
    /// </summary>
    /// <remarks>
    /// Empty by default, which means no forwarded header is trusted. This matters more
    /// than it looks: sites are resolved from the Host header (FR-011a), so trusting a
    /// spoofable X-Forwarded-Host would let a caller choose which site they are talking
    /// to. An operator behind a proxy must name it explicitly.
    /// </remarks>
    [Setting("IP addresses of the reverse proxies whose forwarded headers are trusted. Empty trusts no forwarded header.",
        Allowed = "IP addresses")]
    public IList<string> KnownProxies { get; } = [];

    /// <summary>Trusted proxy networks in CIDR form, e.g. <c>10.0.0.0/8</c>.</summary>
    [Setting("Networks of trusted reverse proxies in CIDR form, e.g. `10.0.0.0/8`, for proxies without a fixed address.",
        Allowed = "CIDR ranges")]
    public IList<string> KnownNetworks { get; } = [];

    /// <summary>
    /// Host names on which the administrative surface answers (009 FR-018a). Empty, the
    /// default, means every host name.
    /// </summary>
    /// <remarks>
    /// When set, <c>/api</c> and <c>/admin</c> on any other host name are treated as public
    /// requests and answered like a missing page, so a client's domain does not expose a
    /// sign-in page. <c>/health</c> answers everywhere. Entries are normalised like site
    /// host names; an invalid one, or one a site holds, stops startup (research S-13).
    /// </remarks>
    [Setting("Host names on which the administrative surface (`/api`, `/admin`) answers, e.g. `admin.example.com`. " +
             "Empty means every host name. On any other host name `/api` and `/admin` answer exactly like a missing page, " +
             "so client domains never show the sign-in page; `/health` answers on every host.",
        Allowed = "host names only: no scheme, port, path or wildcard, and none a site already holds")]
    public IList<string> AdminHostNames { get; } = [];

    /// <summary>The admin host names in normalised form. Empty when none are configured.</summary>
    public IReadOnlySet<string> NormalisedAdminHostNames() =>
        AdminHostNames
            .Select(h => HostNames.TryNormalise(h, out var normalised) ? normalised : null)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The address operators use to reach this installation, e.g.
    /// <c>https://cms.example.com</c>. Optional; empty by default.
    /// </summary>
    /// <remarks>
    /// Used only by the <c>identity recovery-link</c> command to print a complete link. The
    /// server never builds links from a request's Host header (002 FR-011b), so nothing
    /// that serves HTTP reads this.
    /// </remarks>
    [RegularExpression(
        "^(|https?://[^\\s/?#]+(/[^\\s?#]*)?)$",
        ErrorMessage = "Hosting:PublicUrl must be empty or an absolute http(s) URL such as https://cms.example.com.")]
    [Setting("The address people use to reach this installation, including any base path. Used to print complete " +
             "links from `identity recovery-link` and in notification emails; the app never works out its own address " +
             "from request headers.",
        Format = "empty, or an absolute `http(s)` URL such as `https://cms.example.com`")]
    public string PublicUrl { get; set; } = string.Empty;
}

/// <summary>Outbox tuning (contracts/integration-events.md).</summary>
[SettingsSection(SectionName, Area = "outbox", Title = "Outbox")]
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Setting("Run the background dispatcher. Scheduled publishes run through it, so keep it on for at least one instance.")]
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00", ErrorMessage = "Outbox:PollInterval must be between {1} and {2}.")]
    [Setting("How often an idle dispatcher looks for work. It bounds how late a scheduled publish can be.")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    [Range(1, 1000, ErrorMessage = "Outbox:BatchSize must be between {1} and {2}.")]
    [Setting("Events claimed per pass.")]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Per-event handler timeout. Handlers run inside the claim transaction (D-02), so a
    /// slow handler holds a row lock and a connection for this long.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00", ErrorMessage = "Outbox:HandlerTimeout must be between {1} and {2}.")]
    [Setting("How long one event's handler may run. A slow handler holds a database row lock and a connection for this long.")]
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [Range(1, 50, ErrorMessage = "Outbox:MaxAttempts must be between {1} and {2}.")]
    [Setting("Attempts before an event is set aside as failed.")]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>How long completed events are kept before the sweep removes them (FR-020).</summary>
    [Range(typeof(TimeSpan), "00:01:00", "365.00:00:00", ErrorMessage = "Outbox:RetentionPeriod must be between {1} and {2}.")]
    [Setting("How long completed events are kept before the sweep removes them.")]
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>Admin surface configuration.</summary>
/// <remarks>
/// Kept only so the reference keeps documenting a key 0.1.0 shipped until it is removed
/// (#50). Nothing binds it; Program.cs reads it only to warn that it is ignored. The admin
/// is served at <c>RoutePrefixes.Admin</c>.
/// </remarks>
[SettingsSection(SectionName, Area = "admin", Title = "Admin")]
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Never moved the admin; it only changed links, which then led nowhere (#50).</summary>
    [DeprecatedSetting("1.0.0")]
    [Setting("Ignored, with a warning at startup when set. The admin is always served at `/admin`, under `Hosting:BasePath` when one is set; remove this key from your configuration.")]
    public string? PathPrefix { get; set; }
}

/// <summary>
/// Checks what data annotations cannot express for <see cref="HostingOptions"/>.
/// </summary>
/// <remarks>
/// Each admin host name must be a host name by the same rule sites use (009 research S-04):
/// a port, scheme, path or wildcard stops startup, naming the key, rather than silently
/// never matching and leaving the admin surface reachable nowhere.
/// </remarks>
internal sealed class HostingOptionsValidator : IValidateOptions<HostingOptions>
{
    public ValidateOptionsResult Validate(string? name, HostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = options.AdminHostNames
            .Select((value, index) => (value, index))
            .Where(entry => !HostNames.TryNormalise(entry.value, out _))
            .Select(entry =>
                $"Hosting:AdminHostNames:{entry.index} ('{entry.value}') is not a host name. Use a " +
                "name such as admin.example.com, without a scheme, port, path or wildcard.")
            .ToList();

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
