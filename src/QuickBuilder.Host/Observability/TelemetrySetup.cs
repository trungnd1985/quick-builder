using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuickBuilder.Host.Configuration;
using QuickBuilder.Host.Routing;
using QuickBuilder.Shared.Infrastructure;

namespace QuickBuilder.Host.Observability;

/// <summary>
/// Tracing and metrics, instrumented always and exporting nothing by default (FR-031b).
/// </summary>
/// <remarks>
/// The distinction matters. <b>Instrumentation</b> is unconditional: it is what produces
/// the trace ids the logs correlate on (FR-031a), and it costs nothing externally.
/// <b>Export</b> is what sends data somewhere, and it is off unless an operator names an
/// endpoint.
/// <para>
/// With no endpoint configured, no exporter is registered at all — so there is nothing
/// that could attempt a network call. That is why an offline installation behaves
/// identically to a connected one, and why SC-014 can assert zero outbound requests with
/// egress blocked. The constitution's "must run fully offline, no telemetry unless the
/// operator explicitly enables it" is satisfied by construction rather than by a flag
/// someone has to remember to check.
/// </para>
/// </remarks>
internal static class TelemetrySetup
{
    internal static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration
            .GetSection(TelemetryOptions.SectionName)
            .Get<TelemetryOptions>() ?? new TelemetryOptions();

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        // Probes would otherwise dominate every trace view with noise.
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments(RoutePrefixes.Health, StringComparison.OrdinalIgnoreCase);
                    })
                    .AddNpgsql()
                    // Background outbox dispatch starts its own activities so that work is
                    // correlated the same way request work is (D-10).
                    .AddSource(Telemetry.ActivitySourceName);

                if (options.ExportEnabled)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation();

                if (options.ExportEnabled)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
                }
            });

        return services;
    }
}
