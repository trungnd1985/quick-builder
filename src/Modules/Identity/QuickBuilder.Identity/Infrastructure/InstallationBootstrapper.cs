using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.Shared.Infrastructure.Startup;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// Prints the setup code at every startup while the installation is unclaimed (spec FR-004;
/// research R-09).
/// </summary>
/// <remarks>
/// The code is written as one Warning event whose template starts with <c>SETUP CODE</c>, so
/// <c>docker compose logs app | grep "SETUP CODE"</c> finds it. It is the one secret that
/// may reach the logs (FR-004a): it exists only before any account does and is useless
/// once setup completes. The log stream is compact JSON, one object per line, so this is a
/// single event rather than a drawn box, which would break log collectors.
/// </remarks>
internal sealed partial class InstallationBootstrapper(
    IServiceScopeFactory scopes,
    StartupState startupState,
    ILogger<InstallationBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!startupState.IsReady)
        {
            // Migrations have not run (Database:AutoMigrate=false, or they failed). The
            // installation table may not exist; readiness already says what to do.
            LogNotReady(logger);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var installation = await scope.ServiceProvider
            .GetRequiredService<InstallationStore>()
            .EnsureAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!installation.IsClaimed && installation.SetupCode is { } code)
        {
            LogSetupCode(logger, Tokens.FormatSetupCode(code));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "SETUP CODE: {SetupCode}. Open the admin interface and enter this code in the setup " +
            "wizard to create the first administrator. It stops working once setup is complete. " +
            "Finish setup before forwarding these logs off this machine.")]
    private static partial void LogSetupCode(ILogger logger, string setupCode);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Setup code not printed: the database is not ready. It is printed at the next start " +
            "after migrations are applied, or run 'identity setup-code'.")]
    private static partial void LogNotReady(ILogger logger);
}
