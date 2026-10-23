using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using QuickBuilder.Host.Configuration;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Infrastructure.Migrations;
using QuickBuilder.Shared.Infrastructure.Migrations.Platform;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Host;

/// <summary>Startup work the host performs before serving traffic.</summary>
internal static class StartupTasks
{
    /// <summary>
    /// Stops startup when a configured admin host name is held by a site (009 research
    /// S-13).
    /// </summary>
    /// <remarks>
    /// Such a host would be both the administrative surface and a site at once. The check
    /// needs the database, so it runs after migrations rather than with the other option
    /// checks. The host name is operator configuration, not visitor input, so naming it
    /// in the message is how the operator finds the mistake.
    /// </remarks>
    internal static async Task CheckAdminHostNamesAsync(WebApplication app)
    {
        var adminHostNames = app.Services.GetRequiredService<IOptions<HostingOptions>>().Value.NormalisedAdminHostNames();
        if (adminHostNames.Count == 0)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<ISiteDirectory>();

        var taken = new List<string>();
        try
        {
            foreach (var hostName in adminHostNames)
            {
                if (await directory.IsHostNameTakenAsync(hostName, app.Lifetime.ApplicationStopping).ConfigureAwait(false))
                {
                    taken.Add(hostName);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database is not usable yet; migration has already logged why, and readiness
            // will say so. Only a conflict actually found stops startup.
            app.Logger.LogWarning(ex, "Could not check Hosting:AdminHostNames against site host names.");
            return;
        }

        if (taken.Count > 0)
        {
            throw new OptionsValidationException(
                HostingOptions.SectionName,
                typeof(HostingOptions),
                [
                    "Hosting:AdminHostNames lists host names that a site holds: " +
                    string.Join(", ", taken) + ". Remove them from the setting, or from the site.",
                ]);
        }
    }

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
    /// <summary>019: the database belongs to a newer release, or is too old to upgrade directly.</summary>
    internal const int RefusedExitCode = 3;

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
        catch (InstallationIncompatibleException)
        {
            // 019 data-model §2: the runner has logged why at Critical; nothing was changed.
            return RefusedExitCode;
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

            // 009 research S-14: unwind every trusted hop, so a CDN in front of a proxy
            // yields the visitor's host rather than the CDN's. This cannot let an untrusted
            // hop through: processing stops at the first address that is not a configured
            // proxy or network. The default limit of 1 silently ignored the outer hop.
            ForwardLimit = null,
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
