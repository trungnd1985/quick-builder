using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuickBuilder.Shared.Infrastructure.Storage.Local;

/// <summary>
/// Removes the local provider's own leftovers: abandoned writes and probe files (003 research M-04).
/// </summary>
/// <remarks>
/// A <c>.part</c> file older than an hour belongs to a process that died mid-upload: the
/// largest upload the product accepts finishes in seconds (SC-008). This is a provider
/// concern, not part of <see cref="IFileStorage"/> — S3 and Azure each have their own
/// mechanism for abandoned uploads.
/// <para>
/// Walks one first-segment (site) directory at a time, so the job is namespaced by site like
/// the files it cleans (Principle V). Logs counts only, never names.
/// </para>
/// </remarks>
internal sealed class LocalIncomingCleanupService(
    LocalFileStorage storage,
    TimeProvider clock,
    ILogger<LocalIncomingCleanupService> logger) : BackgroundService
{
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromHours(1);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Sweep();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Storage clean-up of incomplete writes failed; retrying next interval.");
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

    /// <summary>Runs one pass. Returns how many files were removed. Exposed for tests.</summary>
    internal int Sweep()
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - MaximumAge;
        var removed = 0;

        if (!Directory.Exists(storage.Root))
        {
            return 0;
        }

        foreach (var probe in Directory.EnumerateFiles(storage.Root, LocalStoragePaths.ProbePrefix + "*"))
        {
            removed += RemoveIfOlder(probe, cutoff);
        }

        removed += SweepIncoming(Path.Combine(storage.Root, LocalStoragePaths.IncomingDirectoryName), cutoff);

        foreach (var site in Directory.EnumerateDirectories(storage.Root))
        {
            removed += SweepIncoming(Path.Combine(site, LocalStoragePaths.IncomingDirectoryName), cutoff);
        }

        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} abandoned temporary storage file(s).", removed);
        }

        return removed;
    }

    private static int SweepIncoming(string incoming, DateTime cutoff)
    {
        if (!Directory.Exists(incoming))
        {
            return 0;
        }

        var removed = 0;
        foreach (var part in Directory.EnumerateFiles(incoming, "*.part"))
        {
            removed += RemoveIfOlder(part, cutoff);
        }

        return removed;
    }

    private static int RemoveIfOlder(string file, DateTime cutoff)
    {
        if (File.GetLastWriteTimeUtc(file) >= cutoff)
        {
            return 0;
        }

        LocalStoragePaths.TryDelete(file);
        return File.Exists(file) ? 0 : 1;
    }
}
