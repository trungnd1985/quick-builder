namespace QuickBuilder.Docs.Settings;

/// <summary>
/// Keys ASP.NET Core and .NET read for themselves, which the product neither defines nor
/// documents (026 contracts/setting-attributes.md §5). Every entry needs a reason; the boot
/// observation test (<c>EveryReadSettingIsDeclared</c>) shows which are actually read.
/// </summary>
internal static class FrameworkAllowList
{
    private const string Kind = "framework";

    public static IReadOnlyList<Exclusion> Entries { get; } =
    [
        new("Logging:*", "Microsoft.Extensions.Logging filters; QuickBuilder logs through Serilog, configured under Serilog.", Kind),
        new("AllowedHosts", "ASP.NET Core host filtering; sites are resolved from their own host names instead.", Kind),
        new("Kestrel:*", "Kestrel endpoints; the image fixes the listening port and TLS is the reverse proxy's job.", Kind),
        new("urls", "Listening addresses; fixed by the image.", Kind),
        new("http_ports", "Listening ports; fixed by the image.", Kind),
        new("https_ports", "HTTPS redirection ports; TLS is the reverse proxy's job.", Kind),
        new("ASPNETCORE_*", "ASP.NET Core host environment variables, read by the framework.", Kind),
        new("DOTNET_*", ".NET runtime environment variables, read by the runtime.", Kind),
        new("applicationName", "Set by the host builder.", Kind),
        new("contentRoot", "Set by the host builder.", Kind),
        new("environment", "Set by the host builder from ASPNETCORE_ENVIRONMENT.", Kind),
        new("webroot", "Set by the host builder.", Kind),
        new("HostingStartupAssemblies", "ASP.NET Core hosting startup; not used by QuickBuilder.", Kind),
        new("HostingStartupExcludeAssemblies", "ASP.NET Core hosting startup; not used by QuickBuilder.", Kind),
        new("PreventHostingStartup", "ASP.NET Core hosting startup; not used by QuickBuilder.", Kind),
        new("detailedErrors", "ASP.NET Core development switch; production never shows detailed errors.", Kind),
        new("captureStartupErrors", "ASP.NET Core host switch.", Kind),
        new("suppressStatusMessages", "ASP.NET Core host switch.", Kind),
        new("shutdownTimeoutSeconds", "ASP.NET Core host switch.", Kind),
        new("preferHostingUrls", "ASP.NET Core host switch.", Kind),
        new("Microsoft:*", "Framework component settings.", Kind),
        new("ForwardedHeaders_Enabled", "ASP.NET Core's own forwarded-headers switch; QuickBuilder configures forwarded headers from Hosting:KnownProxies and Hosting:KnownNetworks.", Kind),
        new("Metrics", "Microsoft.Extensions.Diagnostics metric filters; exporting is controlled by Telemetry:OtlpEndpoint.", Kind),
        new("OTEL_*", "OpenTelemetry SDK environment variables; exporting is controlled by Telemetry:OtlpEndpoint and Telemetry:ServiceName.", Kind),
        new("servicesStartConcurrently", ".NET host switch.", Kind),
        new("servicesStopConcurrently", ".NET host switch.", Kind),
        new("startupTimeoutSeconds", ".NET host switch.", Kind),
        new("Serilog:*", "Serilog's own configuration (sinks, enrichers, filters); the image sets it up. Only the log levels are operator settings.", Kind),
    ];
}
