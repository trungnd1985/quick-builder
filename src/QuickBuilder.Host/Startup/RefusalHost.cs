using System.Text.Json;
using QuickBuilder.Host.Routing;

namespace QuickBuilder.Host;

/// <summary>
/// What the process serves when the startup check refuses the database (019 research RL-08,
/// contracts/installation-version.md §3).
/// </summary>
/// <remarks>
/// The refusal is decided before the host is built, so QuickBuilder's own hosted services are
/// removed and none of them starts: no outbox dispatch, schedule, sweep or backfill ever runs
/// against a database this release does not understand. The request pipeline is a single
/// terminal middleware (no endpoints, so no Minimal API): the probes answer, everything else
/// gets 503. The process stays up, so the probe can say why instead of a restart loop hiding it.
/// </remarks>
internal static class RefusalHost
{
    internal const string Message = "QuickBuilder cannot start on this database. The operator can find the reason in the logs.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Removes every hosted service QuickBuilder registered; the web server's own stays.</summary>
    internal static void RemoveApplicationHostedServices(IServiceCollection services)
    {
        var ours = services
            .Where(d => d.ServiceType == typeof(IHostedService) && IsQuickBuilder(d))
            .ToList();

        foreach (var descriptor in ours)
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>The whole request pipeline of a refused start.</summary>
    internal static void Configure(WebApplication app, string? basePath, string detail)
    {
        // WebApplication inserts authentication and authorization at the front of the pipeline
        // by itself when their services are registered, and Identity's fallback policy would then
        // answer 401 before this pipeline runs. These are the keys it checks to know the app
        // placed them itself; setting them keeps the refusal pipeline the only one.
        var pipeline = (IApplicationBuilder)app;
        pipeline.Properties["__AuthenticationMiddlewareSet"] = true;
        pipeline.Properties["__AuthorizationMiddlewareSet"] = true;

        if (!string.IsNullOrEmpty(basePath))
        {
            app.UsePathBase(basePath);
        }

        app.Run(async context =>
        {
            var response = context.Response;
            response.Headers.CacheControl = "no-store";

            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == RoutePrefixes.Health + "/live")
            {
                response.StatusCode = StatusCodes.Status200OK;
                await response.WriteAsJsonAsync(new { status = "Healthy" }, Json).ConfigureAwait(false);
                return;
            }

            if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == RoutePrefixes.Health + "/ready")
            {
                response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await response.WriteAsJsonAsync(
                    new { status = "Unhealthy", state = "Incompatible", detail, checks = Array.Empty<object>() },
                    Json).ConfigureAwait(false);
                return;
            }

            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            response.ContentType = "text/plain; charset=utf-8";
            await response.WriteAsync(Message).ConfigureAwait(false);
        });
    }

    private static bool IsQuickBuilder(ServiceDescriptor descriptor)
    {
        var type = descriptor.ImplementationType
            ?? descriptor.ImplementationInstance?.GetType()
            ?? descriptor.ImplementationFactory?.Method.DeclaringType;

        return type?.Namespace?.StartsWith("QuickBuilder.", StringComparison.Ordinal) == true;
    }
}
