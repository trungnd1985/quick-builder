using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// Removes identity records that have outlived their purpose (spec FR-029a, SC-012).
/// </summary>
/// <remarks>
/// Modelled on 001's <c>OutboxRetentionService</c>: hourly, because every window here is
/// measured in hours or days. Each table is emptied in batches of <see cref="BatchSize"/>
/// so a first sweep over a year of history never holds one long lock.
/// <para>
/// This is the only way a security event leaves the database. The append-only guard on
/// <see cref="IdentityDbContext"/> rejects deletes through the change tracker; a bulk
/// <c>ExecuteDelete</c> never passes through it, which is exactly the exception intended.
/// </para>
/// </remarks>
internal sealed class IdentityRetentionService(
    IServiceScopeFactory scopes,
    IOptions<IdentityOptions> options,
    TimeProvider clock,
    ILogger<IdentityRetentionService> logger) : BackgroundService
{
    internal const int BatchSize = 10_000;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    /// <summary>A throttle row this quiet carries no information any more.</summary>
    private static readonly TimeSpan ThrottleIdlePeriod = TimeSpan.FromHours(24);

    /// <summary>How long a used, expired or replaced link stays for anyone investigating it.</summary>
    private static readonly TimeSpan SpentLinkGracePeriod = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Identity retention sweep failed; retrying next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs one sweep over every identity table. Exposed for tests.</summary>
    public async Task<RetentionSweep> SweepAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var eventCutoff = now - settings.SecurityEvents.RetentionPeriod;
        var events = await DeleteInBatchesAsync(
            database.SecurityEvents.Where(e => e.OccurredAtUtc < eventCutoff), cancellationToken).ConfigureAwait(false);

        var throttleCutoff = now - ThrottleIdlePeriod;
        var throttles = await DeleteInBatchesAsync(
            database.SignInThrottles.Where(t => t.UpdatedAtUtc < throttleCutoff), cancellationToken).ConfigureAwait(false);

        // A session is finished once revoked or past its absolute expiry; it is kept for a
        // further AbsoluteLifetime so "your sessions" and investigations still see it.
        var sessionCutoff = now - settings.Session.AbsoluteLifetime;
        var sessions = await DeleteInBatchesAsync(
            database.Sessions.Where(s =>
                (s.RevokedAtUtc != null && s.RevokedAtUtc < sessionCutoff) || s.AbsoluteExpiresAtUtc < sessionCutoff),
            cancellationToken).ConfigureAwait(false);

        // Never the live link: one that is unused, not invalidated and not yet expired.
        var linkCutoff = now - SpentLinkGracePeriod;
        var links = await DeleteInBatchesAsync(
            database.SetPasswordLinks.Where(l => l.ExpiresAtUtc < linkCutoff),
            cancellationToken).ConfigureAwait(false);

        var deviceCutoff = now - settings.SignIn.KnownDeviceLifetime;
        var devices = await DeleteInBatchesAsync(
            database.KnownDevices.Where(d => d.RevokedAtUtc != null || d.LastUsedAtUtc < deviceCutoff),
            cancellationToken).ConfigureAwait(false);

        var sweep = new RetentionSweep(events, throttles, sessions, links, devices);
        if (sweep.Total > 0)
        {
            logger.LogInformation(
                "Identity retention removed {SecurityEvents} security event(s), {Throttles} throttle(s), " +
                "{Sessions} session(s), {Links} set-password link(s) and {Devices} known device(s).",
                events, throttles, sessions, links, devices);
        }

        return sweep;
    }

    private static async Task<int> DeleteInBatchesAsync<T>(IQueryable<T> expired, CancellationToken cancellationToken)
    {
        var total = 0;
        int removed;

        do
        {
            removed = await expired.Take(BatchSize).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            total += removed;
        }
        while (removed == BatchSize);

        return total;
    }
}

/// <summary>Rows removed by one retention sweep, per table.</summary>
internal sealed record RetentionSweep(int SecurityEvents, int Throttles, int Sessions, int SetPasswordLinks, int KnownDevices)
{
    public int Total => SecurityEvents + Throttles + Sessions + SetPasswordLinks + KnownDevices;
}
