using System.ComponentModel.DataAnnotations;

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
