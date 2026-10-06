using System.Buffers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Storage.AzureBlob;

/// <summary>
/// The Azure Blob provider (013 contracts/storage-providers.md §4; research MV-19).
/// </summary>
/// <remarks>
/// <para>
/// <b>Create-only and all-or-nothing.</b> A small upload is one Put Blob; a larger one stages
/// blocks and commits the list once. Both carry <c>If-None-Match: *</c>, so an existing blob
/// is refused, and staged blocks are invisible until the commit and expire on their own.
/// </para>
/// <para>
/// <b>Own block ids.</b> The SDK's own chunked upload names blocks by offset, so two
/// simultaneous uploads to one name would overwrite each other's staged blocks before either
/// commits. Random block ids keep each writer's blocks its own, and the conditional commit
/// lets exactly one win.
/// </para>
/// </remarks>
internal sealed class AzureBlobFileStorage : IFileStorage, IFileStorageHealthCheck, IRemoteProbeTarget
{
    /// <summary>The size of a staged block, and the most a single Put Blob carries.</summary>
    internal const int BlockSize = 8 * 1024 * 1024;

    private const int ReadBufferSize = 1024 * 1024;

    private readonly BlobContainerClient _container;

    public AzureBlobFileStorage(IOptions<StorageOptions> options)
        : this(Settings(options))
    {
    }

    internal AzureBlobFileStorage(AzureBlobStorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        KeyPrefix = settings.KeyPrefix;
        _container = AzureBlobClientFactory.Create(settings);
    }

    internal string KeyPrefix { get; }

    internal BlobContainerClient Container => _container;

    public async Task WriteAsync(StorageKey key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blob = _container.GetBlockBlobClient(KeyPrefix + key.Value);
        var buffer = ArrayPool<byte>.Shared.Rent(BlockSize);
        try
        {
            var read = await content.ReadAtLeastAsync(buffer.AsMemory(0, BlockSize), BlockSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (read < BlockSize)
            {
                using var single = new MemoryStream(buffer, 0, read, writable: false);
                await blob.UploadAsync(single, new BlobUploadOptions { Conditions = CreateOnly }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var blocks = new List<string>();
            while (read > 0)
            {
                var id = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                using (var block = new MemoryStream(buffer, 0, read, writable: false))
                {
                    await blob.StageBlockAsync(id, block, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                blocks.Add(id);
                read = await content.ReadAtLeastAsync(buffer.AsMemory(0, BlockSize), BlockSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            }

            await blob.CommitBlockListAsync(blocks, new CommitBlockListOptions { Conditions = CreateOnly }, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (IsExists(ex))
        {
            throw new StorageKeyExistsException(key);
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw Unavailable(ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task<StoredFile?> OpenReadAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(KeyPrefix + key.Value);
        try
        {
            var properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Value;
            var stream = await blob.OpenReadAsync(
                new BlobOpenReadOptions(allowModifications: false)
                {
                    BufferSize = ReadBufferSize,
                    Conditions = new BlobRequestConditions { IfMatch = properties.ETag },
                },
                cancellationToken).ConfigureAwait(false);

            return new StoredFile(stream, properties.ContentLength, properties.LastModified);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // A missing container reads as missing too (contracts/storage-providers.md §7).
            return null;
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw Unavailable(ex);
        }
    }

    public async Task<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _container.GetBlobClient(KeyPrefix + key.Value).ExistsAsync(cancellationToken).ConfigureAwait(false)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
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
            await _container.GetBlobClient(KeyPrefix + key.Value).DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
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

        var pages = _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, KeyPrefix + prefix, cancellationToken)
            .AsPages()
            .GetAsyncEnumerator(cancellationToken);
        await using (pages.ConfigureAwait(false))
        {
            while (await NextPageAsync(pages, cancellationToken).ConfigureAwait(false))
            {
                foreach (var item in pages.Current.Values)
                {
                    var name = item.Name[KeyPrefix.Length..];

                    // Probe objects and anything else outside the grammar are not objects.
                    if (StorageKey.IsValid(name))
                    {
                        yield return new StoredFileInfo(
                            new StorageKey(name),
                            item.Properties.ContentLength ?? 0,
                            item.Properties.LastModified ?? DateTimeOffset.UnixEpoch);
                    }
                }
            }
        }
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

    async Task IRemoteProbeTarget.CreateOnlyAsync(string name, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            await _container.GetBlobClient(KeyPrefix + name)
                .UploadAsync(new BinaryData(content), new BlobUploadOptions { Conditions = CreateOnly }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (IsExists(ex))
        {
            throw new StorageKeyExistsException();
        }
    }

    async Task<byte[]?> IRemoteProbeTarget.ReadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var download = await _container.GetBlobClient(KeyPrefix + name).DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            return download.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    Task IRemoteProbeTarget.DeleteAsync(string name, CancellationToken cancellationToken) =>
        _container.GetBlobClient(KeyPrefix + name).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    ProbeFailure IRemoteProbeTarget.Classify(Exception exception) => Classify(exception);

    internal static ProbeFailure Classify(Exception exception) => exception switch
    {
        RequestFailedException { ErrorCode: "AuthenticationFailed" or "InvalidAuthenticationInfo" or "NoAuthenticationInformation" } => ProbeFailure.Credentials,
        RequestFailedException { ErrorCode: "ContainerNotFound" or "InvalidResourceName" } => ProbeFailure.ContainerMissing,
        RequestFailedException { Status: 403 } => ProbeFailure.AccessDenied,
        AuthenticationFailedException => ProbeFailure.Credentials,
        RequestFailedException { Status: 0 } or HttpRequestException or SocketException or AggregateException => ProbeFailure.Unreachable,
        _ => ProbeFailure.Unreachable,
    };

    /// <summary>
    /// Whether a failure means the service could not be used: consumers see an
    /// <see cref="IOException"/>, as from any other provider, never an SDK type.
    /// </summary>
    /// <remarks>A cancellation the caller did not ask for is the client's own timeout.</remarks>
    internal static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is RequestFailedException or AuthenticationFailedException or HttpRequestException or SocketException or AggregateException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    internal static IOException Unavailable(Exception exception) =>
        new("The Azure Blob service could not complete the request.", exception);

    private static async Task<bool> NextPageAsync(IAsyncEnumerator<Page<BlobItem>> pages, CancellationToken cancellationToken)
    {
        try
        {
            return await pages.MoveNextAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw Unavailable(ex);
        }
    }

    private static BlobRequestConditions CreateOnly => new() { IfNoneMatch = ETag.All };

    private static bool IsExists(RequestFailedException exception) =>
        exception.Status == 412 || exception.ErrorCode == BlobErrorCode.BlobAlreadyExists;

    private static AzureBlobStorageSettings Settings(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Value.AzureBlob;
    }
}

/// <summary>Builds the container client from settings. Separate so credential selection is testable (FR-032).</summary>
internal static class AzureBlobClientFactory
{
    /// <summary>
    /// The identity sources used with <c>AccountUrl</c>, or null with a connection string.
    /// </summary>
    /// <remarks>
    /// The environment's managed or workload identity, its environment variables or the Azure
    /// CLI. Never anything that opens a browser or reads a developer tool's sign-in.
    /// </remarks>
    public static DefaultAzureCredentialOptions? CredentialOptions(AzureBlobStorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ConnectionString.Length > 0
            ? null
            : new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true,
                ExcludeVisualStudioCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeAzureDeveloperCliCredential = true,
            };
    }

    public static BlobContainerClient Create(AzureBlobStorageSettings settings)
    {
        var options = new BlobClientOptions();
        options.Retry.MaxRetries = 2;

        // No request logging at all: a logged URL may carry a SAS signature.
        options.Diagnostics.IsLoggingEnabled = false;

        return CredentialOptions(settings) is { } credential
            ? new BlobContainerClient(new Uri($"{settings.AccountUrl.TrimEnd('/')}/{settings.Container}"), new DefaultAzureCredential(credential), options)
            : new BlobContainerClient(settings.ConnectionString, settings.Container, options);
    }
}
