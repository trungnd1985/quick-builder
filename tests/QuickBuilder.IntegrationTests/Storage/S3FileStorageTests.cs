using System.Security.Cryptography;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.S3;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>What the S3 provider does beyond the conformance suite (013 contracts/storage-providers.md §3, §7).</summary>
public sealed class S3FileStorageTests(S3Fixture s3) : IClassFixture<S3Fixture>
{
    private const int LargeSize = 70 * 1024 * 1024;

    [Fact]
    public async Task A_write_above_64_mib_goes_multipart_and_reads_back_identically()
    {
        using var storage = new S3FileStorage(s3.Settings());
        var bytes = RandomNumberGenerator.GetBytes(LargeSize);
        var key = new StorageKey("site-a/media/large");

        await storage.WriteAsync(key, new MemoryStream(bytes));

        // A multipart object's ETag ends with its part count: 70 MiB in 16 MiB parts is 5.
        using var client = s3.CreateClient();
        var head = await client.GetObjectMetadataAsync(s3.Bucket, storage.KeyPrefix + key.Value);
        Assert.EndsWith("-5\"", head.ETag, StringComparison.Ordinal);

        await using var stored = await storage.OpenReadAsync(key);
        Assert.Equal(LargeSize, stored!.Length);
        using var copy = new MemoryStream();
        await stored.Content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());

        // Create-only holds for multipart too: the completion is refused.
        await Assert.ThrowsAsync<StorageKeyExistsException>(() => storage.WriteAsync(key, new MemoryStream(bytes)));
        Assert.Empty(await PendingUploadsAsync(storage, key));
    }

    [Fact]
    public async Task Cancelling_mid_multipart_leaves_no_object_and_no_pending_upload()
    {
        using var storage = new S3FileStorage(s3.Settings());
        var key = new StorageKey("site-a/media/cancelled");
        using var cancellation = new CancellationTokenSource();

        // Cancel as soon as the multipart upload exists on the service.
        var watcher = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                if ((await PendingUploadsAsync(storage, key)).Count > 0)
                {
                    await cancellation.CancelAsync();
                    return;
                }

                await Task.Delay(10);
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => storage.WriteAsync(key, new MemoryStream(RandomNumberGenerator.GetBytes(LargeSize)), cancellation.Token));
        await watcher;

        Assert.False(await storage.ExistsAsync(key));
        Assert.Empty(await PendingUploadsAsync(storage, key));
    }

    [Fact]
    public async Task The_key_prefix_is_added_on_write_and_removed_from_listings()
    {
        using var storage = new S3FileStorage(s3.Settings("tenant-one/"));
        await storage.WriteAsync(new StorageKey("site-a/media/one"), new MemoryStream([1]));

        using var client = s3.CreateClient();
        var raw = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = s3.Bucket, Prefix = "tenant-one/" });
        Assert.Equal(["tenant-one/site-a/media/one"], raw.S3Objects.Select(o => o.Key));

        var listed = new List<string>();
        await foreach (var entry in storage.ListAsync("site-a/"))
        {
            listed.Add(entry.Key.Value);
        }

        Assert.Equal(["site-a/media/one"], listed);
    }

    [Fact]
    public async Task Probe_objects_are_not_listed_and_health_succeeds_with_path_style_addressing()
    {
        using var storage = new S3FileStorage(s3.Settings());
        Assert.True(await storage.CheckAsync());

        using var client = s3.CreateClient();
        await client.PutObjectAsync(new PutObjectRequest { BucketName = s3.Bucket, Key = storage.KeyPrefix + RemoteProbe.Folder + "left-behind", ContentBody = "x" });

        await foreach (var entry in storage.ListAsync(string.Empty))
        {
            Assert.Fail($"Listed {entry.Key}.");
        }
    }

    [Fact]
    public async Task The_cleanup_aborts_pending_uploads_older_than_an_hour_and_only_those()
    {
        using var storage = new S3FileStorage(s3.Settings());
        using var client = s3.CreateClient();
        var abandonedKey = new StorageKey("site-a/media/abandoned");
        var abandoned = await client.InitiateMultipartUploadAsync(s3.Bucket, storage.KeyPrefix + abandonedKey.Value);

        var clock = new ManualClock();
        var cleanup = new S3AbandonedUploadCleanupService(storage, clock, NullLogger<S3AbandonedUploadCleanupService>.Instance);

        Assert.Equal(0, await cleanup.RunOnceAsync(CancellationToken.None));
        Assert.Single(await PendingUploadsAsync(storage, abandonedKey));

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, await cleanup.RunOnceAsync(CancellationToken.None));
        Assert.Empty(await PendingUploadsAsync(storage, abandonedKey));
        Assert.NotNull(abandoned.UploadId);
    }

    [Fact]
    public async Task Reads_seek_through_ranges_of_a_larger_object()
    {
        using var storage = new S3FileStorage(s3.Settings());
        var bytes = RandomNumberGenerator.GetBytes((3 * 1024 * 1024) + 7);
        var key = new StorageKey("site-a/media/ranged");
        await storage.WriteAsync(key, new MemoryStream(bytes));

        await using var stored = await storage.OpenReadAsync(key);
        var buffer = new byte[1000];
        foreach (var offset in new[] { 2_500_000, 10, 1_048_570, bytes.Length - 1000 })
        {
            stored!.Content.Seek(offset, SeekOrigin.Begin);
            await stored.Content.ReadExactlyAsync(buffer);
            Assert.Equal(bytes.AsSpan(offset, 1000).ToArray(), buffer);
        }
    }

    /// <summary>Pending uploads of one object.</summary>
    private async Task<List<MultipartUpload>> PendingUploadsAsync(S3FileStorage storage, StorageKey key)
    {
        using var client = s3.CreateClient();
        var uploads = await client.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = s3.Bucket, Prefix = storage.KeyPrefix + key.Value });
        return uploads.MultipartUploads ?? [];
    }
}
