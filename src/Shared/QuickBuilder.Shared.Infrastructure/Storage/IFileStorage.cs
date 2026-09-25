namespace QuickBuilder.Shared.Infrastructure.Storage;

/// <summary>
/// Where files live (constitution Principle IV; 003 contracts/storage-provider.md).
/// </summary>
/// <remarks>
/// Exactly the operations the first consumer needs, and nothing that assumes a file system:
/// no paths, no URLs, no provider types (003 research M-02). Each operation maps directly to
/// S3 and Azure Blob storage, which is what lets those providers be added without changing
/// any consumer.
/// </remarks>
public interface IFileStorage
{
    /// <summary>
    /// Stores <paramref name="content"/> under <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// <b>Create-only</b>: throws <see cref="StorageKeyExistsException"/> when the key exists.
    /// <b>All-or-nothing</b>: if this throws or is cancelled, the key does not exist
    /// afterwards and no reader ever saw a partial object. Reads <paramref name="content"/> to
    /// its end and does not dispose it.
    /// </remarks>
    Task WriteAsync(StorageKey key, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a stored object, or returns null when the key does not exist.
    /// </summary>
    /// <remarks>
    /// The caller disposes the result. <see cref="StoredFile.Content"/> is always seekable,
    /// which is how partial reads work: seek, then read.
    /// </remarks>
    Task<StoredFile?> OpenReadAsync(StorageKey key, CancellationToken cancellationToken = default);

    /// <summary>Whether the key exists. No side effects.</summary>
    Task<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes the key. Idempotent: deleting a missing key succeeds.</summary>
    Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every committed object whose key starts with <paramref name="prefix"/>, in any order.
    /// </summary>
    /// <remarks>
    /// Never includes incomplete writes. Streams: it does not load a whole listing into
    /// memory. <paramref name="prefix"/> must satisfy <see cref="StorageKey.IsValidPrefix"/>.
    /// </remarks>
    IAsyncEnumerable<StoredFileInfo> ListAsync(string prefix, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether the configured storage can currently be written to and read from (003 research M-22).
/// </summary>
/// <remarks>
/// Separate from <see cref="IFileStorage"/> on purpose: consumers never need it, and each
/// back-end proves health differently. Readiness calls it; nothing else should.
/// </remarks>
public interface IFileStorageHealthCheck
{
    /// <summary>True when a write, read and delete round trip succeeds. Never throws.</summary>
    Task<bool> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>An opened stored object. Dispose it to release the underlying handle.</summary>
public sealed class StoredFile(Stream content, long length, DateTimeOffset lastModifiedUtc) : IDisposable, IAsyncDisposable
{
    /// <summary>The bytes. Always seekable.</summary>
    public Stream Content { get; } = content ?? throw new ArgumentNullException(nameof(content));

    public long Length { get; } = length;

    public DateTimeOffset LastModifiedUtc { get; } = lastModifiedUtc;

    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>One entry of a listing.</summary>
public sealed record StoredFileInfo(StorageKey Key, long Length, DateTimeOffset LastModifiedUtc);

/// <summary>Thrown by <see cref="IFileStorage.WriteAsync"/> when the key already exists.</summary>
public sealed class StorageKeyExistsException : IOException
{
    public StorageKeyExistsException()
    {
    }

    public StorageKeyExistsException(string message) : base(message)
    {
    }

    public StorageKeyExistsException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public StorageKeyExistsException(StorageKey key)
        : base($"An object already exists under '{key}'. Storage is create-only.")
    {
    }
}
