using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Storage.S3;

/// <summary>
/// The S3-compatible provider (013 contracts/storage-providers.md §3; research MV-18).
/// </summary>
/// <remarks>
/// <para>
/// <b>Create-only and all-or-nothing.</b> Every write carries <c>If-None-Match: *</c>, so the
/// service refuses an existing key, and an object only becomes visible when its single PUT
/// or its multipart completion succeeds. A failed or cancelled multipart upload is aborted;
/// one the process could not abort is removed by <see cref="S3AbandonedUploadCleanupService"/>.
/// </para>
/// <para>
/// <b>Secrets.</b> The secret key is handed to the SDK and never formatted into a message;
/// failures are reported by kind, not by the service's text (FR-033).
/// </para>
/// </remarks>
internal sealed class S3FileStorage : IFileStorage, IFileStorageHealthCheck, IRemoteProbeTarget, IDisposable
{
    /// <summary>Uploads up to this size go in one PUT; larger ones are multipart.</summary>
    internal const long SinglePutLimit = 64L * 1024 * 1024;

    internal const int PartSize = 16 * 1024 * 1024;

    // A concurrent conditional write can be answered 409 instead of 412; S3 asks for a retry,
    // which then either succeeds or is refused as existing.
    private const int ConflictAttempts = 3;

    private readonly AmazonS3Client _client;

    public S3FileStorage(IOptions<StorageOptions> options)
        : this(Settings(options))
    {
    }

    internal S3FileStorage(S3StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Bucket = settings.Bucket;
        KeyPrefix = settings.KeyPrefix;
        _client = S3ClientFactory.Create(settings);
    }

    internal string Bucket { get; }

    internal string KeyPrefix { get; }

    internal IAmazonS3 Client => _client;

    public async Task WriteAsync(StorageKey key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var spool = await S3UploadSpool.CreateAsync(content, cancellationToken).ConfigureAwait(false);
        await using (spool.ConfigureAwait(false))
        {
            for (var attempt = 1; ; attempt++)
            {
                spool.Position = 0;
                try
                {
                    if (spool.Length <= SinglePutLimit)
                    {
                        await PutAsync(ObjectName(key.Value), spool, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await MultipartAsync(ObjectName(key.Value), spool, cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    throw new StorageKeyExistsException(key);
                }
                catch (AmazonS3Exception ex) when (IsConditionalConflict(ex) && attempt < ConflictAttempts)
                {
                    // Lost a race to another conditional write; try again to learn who won.
                }
                catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
                {
                    throw Unavailable(ex);
                }
            }
        }
    }

    public async Task<StoredFile?> OpenReadAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        var name = ObjectName(key.Value);
        GetObjectMetadataResponse head;
        try
        {
            head = await _client.GetObjectMetadataAsync(Bucket, name, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw Unavailable(ex);
        }

        var length = head.ContentLength;
        var stream = new S3RangeReadStream(new RangeSource(_client, Bucket, name, head.ETag), length);
        return new StoredFile(stream, length, Utc(head.LastModified));
    }

    public async Task<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(Bucket, ObjectName(key.Value), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw Unavailable(ex);
        }
    }

    public async Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteObjectAsync(Bucket, ObjectName(key.Value), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw Unavailable(ex);
        }
    }

    public async IAsyncEnumerable<StoredFileInfo> ListAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!StorageKey.IsValidPrefix(prefix))
        {
            throw new ArgumentException($"'{prefix}' is not a valid storage key prefix.", nameof(prefix));
        }

        var request = new ListObjectsV2Request { BucketName = Bucket, Prefix = KeyPrefix + prefix };
        ListObjectsV2Response page;
        do
        {
            page = await ListPageAsync(request, cancellationToken).ConfigureAwait(false);
            foreach (var entry in page.S3Objects ?? [])
            {
                var name = entry.Key[KeyPrefix.Length..];

                // Probe objects and anything else outside the grammar are not objects.
                if (StorageKey.IsValid(name))
                {
                    yield return new StoredFileInfo(new StorageKey(name), entry.Size ?? 0, Utc(entry.LastModified));
                }
            }

            request.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);
    }

    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await RemoteProbe.RunAsync(this, RemoteProbe.HealthBudget, checkConditionalWrite: false, cancellationToken).ConfigureAwait(false)
                == ProbeFailure.None;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose() => _client.Dispose();

    async Task IRemoteProbeTarget.CreateOnlyAsync(string name, byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content);
        try
        {
            await PutAsync(KeyPrefix + name, stream, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed || IsConditionalConflict(ex))
        {
            throw new StorageKeyExistsException();
        }
    }

    async Task<byte[]?> IRemoteProbeTarget.ReadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetObjectAsync(Bucket, KeyPrefix + name, cancellationToken).ConfigureAwait(false);
            using var copy = new MemoryStream();
            await response.ResponseStream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            return copy.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorCode != "NoSuchBucket")
        {
            return null;
        }
    }

    Task IRemoteProbeTarget.DeleteAsync(string name, CancellationToken cancellationToken) =>
        _client.DeleteObjectAsync(Bucket, KeyPrefix + name, cancellationToken);

    ProbeFailure IRemoteProbeTarget.Classify(Exception exception) => Classify(exception);

    internal static ProbeFailure Classify(Exception exception) => exception switch
    {
        AmazonS3Exception { ErrorCode: "InvalidAccessKeyId" or "SignatureDoesNotMatch" or "InvalidToken" or "ExpiredToken" } => ProbeFailure.Credentials,
        AmazonS3Exception { ErrorCode: "NoSuchBucket" } => ProbeFailure.ContainerMissing,
        AmazonS3Exception { StatusCode: HttpStatusCode.Forbidden } => ProbeFailure.AccessDenied,
        AmazonServiceException { InnerException: HttpRequestException or SocketException or IOException } => ProbeFailure.Unreachable,
        HttpRequestException or SocketException => ProbeFailure.Unreachable,

        // The ambient chain found nothing to sign with.
        AmazonClientException when exception.Message.Contains("credential", StringComparison.OrdinalIgnoreCase) => ProbeFailure.Credentials,
        _ => ProbeFailure.Unreachable,
    };

    /// <summary>
    /// Whether a failure means the service could not be used: consumers see an
    /// <see cref="IOException"/>, as from any other provider, never an SDK type.
    /// </summary>
    /// <remarks>A cancellation the caller did not ask for is the SDK's own timeout.</remarks>
    internal static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is AmazonServiceException or AmazonClientException or HttpRequestException or SocketException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    internal static IOException Unavailable(Exception exception) =>
        new("The S3 service could not complete the request.", exception);

    private async Task<ListObjectsV2Response> ListPageAsync(ListObjectsV2Request request, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw Unavailable(ex);
        }
    }

    private string ObjectName(string key) => KeyPrefix + key;

    private Task<PutObjectResponse> PutAsync(string name, Stream content, CancellationToken cancellationToken) =>
        _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = Bucket,
                Key = name,
                InputStream = content,
                AutoCloseStream = false,
                AutoResetStreamPosition = false,
                IfNoneMatch = "*",
            },
            cancellationToken);

    private async Task MultipartAsync(string name, Stream spool, CancellationToken cancellationToken)
    {
        // Not the caller's token: cancelled mid-request, the upload would exist on the service
        // with no id here to abort it by. Cancellation is honoured as soon as it has one.
        var upload = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = Bucket, Key = name },
            CancellationToken.None).ConfigureAwait(false);

        var buffer = ArrayPool<byte>.Shared.Rent(PartSize);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = new List<PartETag>();
            for (var number = 1; ; number++)
            {
                var read = await spool.ReadAtLeastAsync(buffer.AsMemory(0, PartSize), PartSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                using var part = new MemoryStream(buffer, 0, read, writable: false);
                var response = await _client.UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = Bucket,
                        Key = name,
                        UploadId = upload.UploadId,
                        PartNumber = number,
                        PartSize = read,
                        InputStream = part,
                    },
                    cancellationToken).ConfigureAwait(false);

                parts.Add(new PartETag(number, response.ETag));
            }

            await _client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = Bucket,
                    Key = name,
                    UploadId = upload.UploadId,
                    PartETags = parts,
                    IfNoneMatch = "*",
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await AbortQuietlyAsync(name, upload.UploadId).ConfigureAwait(false);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task AbortQuietlyAsync(string name, string uploadId)
    {
        try
        {
            // Not the caller's token: a cancelled write must still clean up after itself.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = Bucket, Key = name, UploadId = uploadId },
                timeout.Token).ConfigureAwait(false);
        }
        catch (Exception) // The hourly clean-up aborts whatever this could not.
        {
        }
    }

    private static bool IsConditionalConflict(AmazonS3Exception exception) =>
        exception.StatusCode == HttpStatusCode.Conflict && exception.ErrorCode == "ConditionalRequestConflict";

    private static DateTimeOffset Utc(DateTime? value) => value switch
    {
        null => DateTimeOffset.UnixEpoch,
        { Kind: DateTimeKind.Unspecified } unspecified => new DateTimeOffset(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)),
        { } known => new DateTimeOffset(known.ToUniversalTime()),
    };

    private static S3StorageSettings Settings(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Value.S3;
    }

    /// <summary>One object's ranges, pinned to the version that was opened.</summary>
    private sealed class RangeSource(IAmazonS3 client, string bucket, string name, string etag) : IRangeSource
    {
        public async Task ReadRangeAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await client.GetObjectAsync(
                    new GetObjectRequest
                    {
                        BucketName = bucket,
                        Key = name,
                        ByteRange = new ByteRange(offset, offset + destination.Length - 1),
                        EtagToMatch = etag,
                    },
                    cancellationToken).ConfigureAwait(false);

                await response.ResponseStream.ReadExactlyAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
            {
                // Including the object having been deleted since it was opened.
                throw Unavailable(ex);
            }
        }
    }
}

/// <summary>Builds the S3 client from settings. Separate so credential selection is testable (FR-032).</summary>
internal static class S3ClientFactory
{
    public static AmazonS3Config Config(S3StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var config = new AmazonS3Config
        {
            ForcePathStyle = settings.ForcePathStyle,

            // Many S3-compatible services reject the newer default checksums.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            RetryMode = RequestRetryMode.Standard,

            // Retries after the first attempt: three attempts in all.
            MaxErrorRetry = 2,
        };

        if (settings.ServiceUrl.Length > 0)
        {
            config.ServiceURL = settings.ServiceUrl;
            config.AuthenticationRegion = settings.Region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }

        return config;
    }

    /// <summary>Explicit keys, or null for the SDK's ambient chain (environment, web identity, instance profile).</summary>
    public static AWSCredentials? Credentials(S3StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.HasKeys ? new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey) : null;
    }

    public static AmazonS3Client Create(S3StorageSettings settings) =>
        Credentials(settings) is { } credentials
            ? new AmazonS3Client(credentials, Config(settings))
            : new AmazonS3Client(Config(settings));
}
