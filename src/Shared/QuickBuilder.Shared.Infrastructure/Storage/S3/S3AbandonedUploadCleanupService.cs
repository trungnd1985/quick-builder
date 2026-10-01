using Amazon.S3.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuickBuilder.Shared.Infrastructure.Storage.S3;

/// <summary>
/// Aborts multipart uploads left behind under the key prefix (013 contracts/storage-providers.md §3.3).
/// </summary>
/// <remarks>
/// A write aborts its own multipart upload when it fails, but a process that dies mid-write
/// cannot. Parts of an unfinished upload are never visible as an object; they only cost
/// storage until aborted. An hour is far longer than any upload under the size limit takes.
/// </remarks>
internal sealed class S3AbandonedUploadCleanupService(
    S3FileStorage storage,
    TimeProvider clock,
    ILogger<S3AbandonedUploadCleanupService> logger) : BackgroundService
{
    internal static readonly TimeSpan Age = TimeSpan.FromHours(1);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                var aborted = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (aborted > 0)
                {
                    logger.LogInformation("Aborted {Count} abandoned S3 multipart upload(s).", aborted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The type only: a service message may echo request details.
                logger.LogWarning("The S3 abandoned-upload clean-up failed ({ErrorType}); retrying next interval.", ex.GetType().Name);
            }
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Aborts every pending upload under the key prefix older than an hour.</summary>
    internal Task<int> RunOnceAsync(CancellationToken cancellationToken) => RunOnceAsync(storage.KeyPrefix, cancellationToken);

    /// <summary>
    /// Aborts pending uploads whose names start with <paramref name="prefix"/>. Exposed for tests:
    /// MinIO lists multipart uploads only by exact object name (it expires stale ones itself
    /// after 24 hours), so a test there names the object.
    /// </summary>
    internal async Task<int> RunOnceAsync(string prefix, CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - Age;
        var request = new ListMultipartUploadsRequest { BucketName = storage.Bucket, Prefix = prefix };
        var aborted = 0;

        ListMultipartUploadsResponse page;
        do
        {
            page = await storage.Client.ListMultipartUploadsAsync(request, cancellationToken).ConfigureAwait(false);
            foreach (var upload in page.MultipartUploads ?? [])
            {
                if (upload.Initiated is { } initiated && new DateTimeOffset(initiated.ToUniversalTime()) < cutoff)
                {
                    await storage.Client.AbortMultipartUploadAsync(
                        new AbortMultipartUploadRequest { BucketName = storage.Bucket, Key = upload.Key, UploadId = upload.UploadId },
                        cancellationToken).ConfigureAwait(false);
                    aborted++;
                }
            }

            request.KeyMarker = page.NextKeyMarker;
            request.UploadIdMarker = page.NextUploadIdMarker;
        }
        while (page.IsTruncated == true);

        return aborted;
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
