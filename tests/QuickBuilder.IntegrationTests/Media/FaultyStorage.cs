using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.IntegrationTests.Media;

/// <summary>
/// Wraps the real provider and injects failures on demand, for the consistency and sweep
/// tests (003 SC-006). Everything not switched on passes straight through.
/// </summary>
internal sealed class FaultyStorage(IFileStorage inner) : IFileStorage
{
    /// <summary>When set, <see cref="WriteAsync"/> reads this many bytes, then throws it.</summary>
    public (int AfterBytes, Exception Error)? FailWrite { get; set; }

    /// <summary>When set, <see cref="DeleteAsync"/> throws it.</summary>
    public Exception? FailDelete { get; set; }

    /// <summary>How many times <see cref="OpenReadAsync"/> has been called.</summary>
    public int Opens { get; private set; }

    public async Task WriteAsync(StorageKey key, Stream content, CancellationToken cancellationToken = default)
    {
        if (FailWrite is { } failure)
        {
            var buffer = new byte[Math.Max(1, failure.AfterBytes)];
            _ = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            throw failure.Error;
        }

        await inner.WriteAsync(key, content, cancellationToken).ConfigureAwait(false);
    }

    public Task<StoredFile?> OpenReadAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        Opens++;
        return inner.OpenReadAsync(key, cancellationToken);
    }

    public Task<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default) =>
        inner.ExistsAsync(key, cancellationToken);

    public Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default) =>
        FailDelete is { } error ? Task.FromException(error) : inner.DeleteAsync(key, cancellationToken);

    public IAsyncEnumerable<StoredFileInfo> ListAsync(string prefix, CancellationToken cancellationToken = default) =>
        inner.ListAsync(prefix, cancellationToken);

    /// <summary>An <see cref="IOException"/> the platform reports for a full disk.</summary>
    public static IOException DiskFull() =>
        new("There is not enough space on the disk.", OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28);
}
