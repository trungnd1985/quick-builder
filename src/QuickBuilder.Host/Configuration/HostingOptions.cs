using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Host.Configuration;

/// <summary>
/// How the application sits behind a reverse proxy (FR-012, D-12).
/// </summary>
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>
    /// Base path when the app is served under a sub-path, e.g. <c>/cms</c>. Empty means
    /// the root.
    /// </summary>
    [RegularExpression("^(/[A-Za-z0-9._~-]+)*$", ErrorMessage = "Hosting:BasePath must be empty or start with '/'.")]
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
    public IList<string> KnownProxies { get; } = [];

    /// <summary>Trusted proxy networks in CIDR form, e.g. <c>10.0.0.0/8</c>.</summary>
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
    public string PublicUrl { get; set; } = string.Empty;
}

/// <summary>Outbox tuning (contracts/integration-events.md).</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Per-event handler timeout. Handlers run inside the claim transaction (D-02), so a
    /// slow handler holds a row lock and a connection for this long.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);

    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>How long completed events are kept before the sweep removes them (FR-020).</summary>
    [Range(typeof(TimeSpan), "00:01:00", "365.00:00:00")]
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>Admin surface configuration.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Path prefix the admin single-page app is served from (FR-042). Reserved ahead of
    /// the public catch-all.
    /// </summary>
    [Required]
    [RegularExpression("^/[A-Za-z0-9._~-]+$", ErrorMessage = "Admin:PathPrefix must start with '/'.")]
    public string PathPrefix { get; set; } = "/admin";
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
