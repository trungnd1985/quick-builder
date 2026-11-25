using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Host.Routing;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.Startup;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.Host.Health;

/// <summary>Shape of a health response (contracts/admin-api.yaml).</summary>
public sealed record HealthCheckResult(string Name, string Status);

/// <summary>Health report returned by both probes.</summary>
public sealed record HealthReport(
    string Status,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Detail,
    IReadOnlyList<HealthCheckResult> Checks);

/// <summary>
/// Liveness and readiness (FR-027, D-17).
/// </summary>
/// <remarks>
/// The two probes answer different questions, and conflating them is how an orchestrator
/// ends up killing a healthy process during a database blip.
/// <list type="bullet">
/// <item><b>Liveness</b> — can this process serve requests at all? Depends on nothing
/// external. If it fails, restarting helps.</item>
/// <item><b>Readiness</b> — should traffic be sent here? Needs valid configuration,
/// completed migrations and a reachable database. If it fails, restarting does not help;
/// waiting might (SC-010).</item>
/// </list>
/// <para>
/// This controller reaches the database through <see cref="IModuleDatabaseProvider"/>, not
/// through any module's DbContext. The host must not know what a module's data looks like
/// (FR-001, FR-022) — and module contexts are internal anyway, so the boundary is enforced
/// by the compiler as well as by the architecture tests.
/// </para>
/// <para>
/// The database check is hand-rolled rather than taking AspNetCore.HealthChecks.NpgSql:
/// it is one call, and that package's current release targets an older framework. Noted
/// as a deviation from research.md D-17.
/// </para>
/// </remarks>
[ApiController]
// Orchestrator probes never authenticate, whatever the setup state (002 FR-006,
// contracts/authorization.md §4).
[AllowAnonymous]
[Route(RoutePrefixes.Health)]
public sealed class HealthController(
    StartupState startupState,
    IEnumerable<IModuleDatabaseProvider> moduleDatabases,
    IFileStorageHealthCheck storage,
    IServiceProvider services) : ControllerBase
{
    /// <summary>How long the storage check may take before it counts as unhealthy (003 research M-22).</summary>
    internal static readonly TimeSpan StorageCheckTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Succeeds whenever the process can serve requests.</summary>
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "Healthy" });

    /// <summary>Succeeds only when the application should receive traffic.</summary>
    [HttpGet("ready")]
    public async Task<IActionResult> ReadyAsync(CancellationToken cancellationToken)
    {
        var checks = new List<HealthCheckResult>();
        var allHealthy = true;

        foreach (var provider in moduleDatabases)
        {
            var description = provider.Describe();
            var healthy = await provider.CanConnectAsync(services, cancellationToken).ConfigureAwait(false);

            allHealthy &= healthy;
            checks.Add(new HealthCheckResult(
                $"postgres:{description.Schema}",
                healthy ? "Healthy" : "Unhealthy"));
        }

        // 003 research M-22: a media volume that vanishes or turns read-only after startup
        // must take the instance out of rotation, not leave it "ready" while uploads fail.
        var storageHealthy = await CheckStorageAsync(cancellationToken).ConfigureAwait(false);
        allHealthy &= storageHealthy;
        checks.Add(new HealthCheckResult("storage", storageHealthy ? "Healthy" : "Unhealthy"));

        var ready = startupState.IsReady && allHealthy;

        var report = new HealthReport(
            ready ? "Healthy" : "Unhealthy",
            startupState.Phase.ToString(),
            startupState.Detail,
            checks);

        return ready ? Ok(report) : StatusCode(StatusCodes.Status503ServiceUnavailable, report);
    }

    private async Task<bool> CheckStorageAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StorageCheckTimeout);

        try
        {
            // WaitAsync as well as the token: a provider that ignores cancellation must still
            // not hold the probe past its budget.
            return await storage.CheckAsync(timeout.Token)
                .WaitAsync(StorageCheckTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

}
