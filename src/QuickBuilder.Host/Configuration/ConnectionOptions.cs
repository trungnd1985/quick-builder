using System.ComponentModel.DataAnnotations;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Host.Configuration;

/// <summary>
/// The database connection (FR-028, FR-029).
/// </summary>
/// <remarks>
/// Required with no default. PostgreSQL is the one external dependency the constitution
/// allows, and there is no sensible fallback — an installation without it is not an
/// installation. Validated on start, so a missing value stops the process before it
/// listens, naming the key, rather than failing later as a confusing connection error.
/// </remarks>
[SettingsSection(SectionName, Area = "required", Title = "Required")]
public sealed class ConnectionOptions
{
    public const string SectionName = "ConnectionStrings";

    [Required(AllowEmptyStrings = false, ErrorMessage =
        "ConnectionStrings:Default is required. Set it in appsettings.json or as the " +
        "environment variable ConnectionStrings__Default.")]
    [Setting("The PostgreSQL connection string. There is no default on purpose: PostgreSQL is the one external " +
             "dependency and there is no sensible fallback.",
        Secret = true,
        Format = "an Npgsql connection string, e.g. `Host=db;Port=5432;Database=quickbuilder;Username=quickbuilder;Password=<password>`")]
    public string Default { get; set; } = string.Empty;
}

/// <summary>
/// Telemetry (FR-031b).
/// </summary>
/// <remarks>
/// Tracing and metrics are always instrumented; only export is optional, and it is off.
/// With no endpoint configured, no exporter is registered and nothing attempts a network
/// call — which is what makes an offline installation behave identically to a connected
/// one, and what SC-014 verifies.
/// </remarks>
[SettingsSection(SectionName, Area = "telemetry", Title = "Telemetry")]
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    /// OTLP endpoint. Empty (the default) means no exporter and no outbound traffic.
    /// </summary>
    [Setting("The OpenTelemetry (OTLP) collector to export traces and metrics to. Empty means no exporter is " +
             "registered and nothing leaves the process.",
        Format = "empty, or the collector's URL, e.g. `http://otel-collector:4317`")]
    public string OtlpEndpoint { get; set; } = string.Empty;

    public bool ExportEnabled => !string.IsNullOrWhiteSpace(OtlpEndpoint);

    [Setting("The service name on exported traces and metrics.")]
    public string ServiceName { get; set; } = "quickbuilder";
}
