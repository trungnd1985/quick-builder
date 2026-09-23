using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Infrastructure.Security;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// Startup warnings an operator should see (research R-03 safeguard 1, research R-04).
/// </summary>
/// <remarks>
/// Warnings, not failures: the installation still works, but the operator learns why
/// sign-in feels slow, or that credentials may cross the network unencrypted.
/// </remarks>
internal sealed class IdentityStartupChecks(
    Pbkdf2PasswordHasher hasher,
    IOptions<IdentityOptions> options,
    ILogger<IdentityStartupChecks> logger) : IHostedService
{
    private static readonly TimeSpan SlowHashThreshold = TimeSpan.FromMilliseconds(400);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.AllowInsecureHttp)
        {
            logger.LogWarning(
                "Identity:AllowInsecureHttp is enabled: passwords may be sent over plain HTTP from other " +
                "machines. Serve this installation through HTTPS and turn the setting off.");
        }

        // Off the startup path: timing a hash is informational and must not delay readiness.
        _ = Task.Run(
            () =>
            {
                var elapsed = hasher.MeasureOnce();
                if (elapsed > SlowHashThreshold)
                {
                    logger.LogWarning(
                        "Password hashing took {Elapsed} ms at {Iterations} iterations, above the {Threshold} ms " +
                        "this hardware needs to meet the sign-in target. Sign-in will feel slow; give the " +
                        "application more CPU rather than lowering the iteration count.",
                        (int)elapsed.TotalMilliseconds,
                        hasher.Iterations,
                        (int)SlowHashThreshold.TotalMilliseconds);
                }
            },
            CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
