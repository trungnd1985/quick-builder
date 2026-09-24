using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using QuickBuilder.Host.Configuration;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Infrastructure.Migrations;

namespace QuickBuilder.Host;

/// <summary>Startup work the host performs before serving traffic.</summary>
internal static class StartupTasks
{
    /// <summary>
    /// Applies migrations (FR-023), or records why it did not.
    /// </summary>
    /// <remarks>
    /// A migration failure deliberately does not crash the process: readiness stays
    /// unhealthy with a reason an operator can read from the probe. A crash-looping
    /// container hides the cause behind restart noise.
    /// </remarks>
    internal static async Task RunMigrationsAsync(WebApplication app)
    {
        var runner = app.Services.GetRequiredService<ModuleMigrationRunner>();

        try
        {
            await runner.RunAsync(force: false, app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(
                ex,
                "Startup migration failed. The application will start but will never report ready.");
        }
    }
}

/// <summary>
/// The <c>migrate</c> command: applies migrations and exits (FR-023).
/// </summary>
/// <remarks>
/// For operators who set <c>Database:AutoMigrate=false</c> and would rather apply schema
/// changes as a deliberate, observable step — typically once, before rolling out new
/// application instances. Registered through <see cref="IHostCommand"/> like every other
/// command (002 research R-16), so the host has no special case for it.
/// </remarks>
internal sealed class MigrateCommand(ModuleMigrationRunner runner, ILogger<MigrateCommand> logger) : IHostCommand
{
    public string Name => "migrate";

    public async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            // force: true — the operator asked for migrations explicitly, so the
            // AutoMigrate setting is not consulted.
            await runner.RunAsync(force: true, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Migrations applied. Exiting.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Migration failed.");
            return 1;
        }
    }
}

/// <summary>Forwarded-headers configuration (FR-012, D-12).</summary>
internal static class ProxySetup
{
    internal static ForwardedHeadersOptions BuildOptions(HostingOptions hosting)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,
        };

        // Defaults contain a loopback entry; clear it so only what an operator configured
        // is trusted. Sites are resolved from the Host header (FR-011a), so a spoofable
        // X-Forwarded-Host would let a caller pick which site they are talking to.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in hosting.KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in hosting.KnownNetworks)
        {
            var parts = network.Split('/', 2);
            if (parts.Length == 2
                && IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], out var length))
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
            }
        }

        return options;
    }
}
