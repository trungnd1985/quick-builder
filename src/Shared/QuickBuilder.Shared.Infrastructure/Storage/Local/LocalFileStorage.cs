using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Storage.Local;

/// <summary>
/// The local file-system provider: the constitution's default (Principle IV; 003 research M-04).
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomic writes.</b> Bytes are streamed to a temporary <c>.part</c> file on the same file
/// system, flushed to disk, then moved into place without overwriting. A rename within one
/// file system is atomic, so a key is either absent or complete — even if the process dies
/// mid-write (FR-011).
/// </para>
/// <para>
/// <b>Namespaced by site.</b> Temporary files live under the key's first segment, which for
/// module keys is the site id, so bytes in flight are namespaced by site like everything
/// else (Principle V).
/// </para>
/// <para>
/// <b>Contained.</b> Besides the key grammar, every resolved path is checked to lie under
/// the root. Two independent guards, because a traversal bug here reads or deletes
/// arbitrary files.
/// </para>
/// </remarks>
internal sealed class LocalFileStorage : IFileStorage, IFileStorageHealthCheck
{
    private const int BufferSize = 81_920;

    // rw-r----- : no execute bit for anyone (FR-016, Principle IX).
    private const UnixFileMode FileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = LocalStoragePaths.Canonicalize(options.Value.Local.Path);
    }

    /// <summary>The canonical root directory. For tests and the cleanup service.</summary>
    internal string Root => _root;

    public async Task WriteAsync(StorageKey key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var final = PathFor(key);
        if (File.Exists(final))
        {
            throw new StorageKeyExistsException(key);
        }

        var incoming = IncomingDirectoryFor(key);
        Directory.CreateDirectory(incoming);
        var part = Path.Combine(incoming, Guid.NewGuid().ToString("N") + ".part");

        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = System.IO.FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                BufferSize = BufferSize,
            };

            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = FileMode;
            }

            var target = new FileStream(part, streamOptions);
            await using (target.ConfigureAwait(false))
            {
                await content.CopyToAsync(target, BufferSize, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);

                // Durable before visible: the rename below must never publish bytes that are
                // still only in the page cache.
                target.Flush(flushToDisk: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(final)!);

            try
            {
                File.Move(part, final, overwrite: false);
            }
            catch (IOException) when (File.Exists(final))
            {
                // Lost a race with another writer of the same key.
                throw new StorageKeyExistsException(key);
            }
        }
        finally
        {
            // After a successful move the .part is gone and this is a no-op.
            LocalStoragePaths.TryDelete(part);
        }
    }

    public Task<StoredFile?> OpenReadAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);

        try
        {
            // FileShare.Delete: a delete while someone is downloading must not fail, and the
            // download in progress may finish (spec edge case).
            var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = System.IO.FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = BufferSize,
            });

            var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return Task.FromResult<StoredFile?>(new StoredFile(stream, stream.Length, lastModified));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<StoredFile?>(null);
        }
    }

    public Task<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(PathFor(key)));

    public Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            File.Delete(PathFor(key));
        }
        catch (DirectoryNotFoundException)
        {
            // Idempotent: nothing there to delete.
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredFileInfo> ListAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!StorageKey.IsValidPrefix(prefix))
        {
            throw new ArgumentException($"'{prefix}' is not a valid storage key prefix.", nameof(prefix));
        }

        // Start at the deepest directory the prefix names completely, so listing one site
        // never walks the others.
        var lastSlash = prefix.LastIndexOf('/');
        var start = lastSlash < 0 ? _root : PathForRelative(prefix[..lastSlash]);
        if (!Directory.Exists(start))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!string.Equals(Path.GetFileName(child), LocalStoragePaths.IncomingDirectoryName, StringComparison.Ordinal))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var relative = Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/');

                // Probe files and anything else that is not a key (never written through this
                // provider) is not a stored object.
                if (!StorageKey.IsValid(relative) || !relative.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    continue;
                }

                yield return new StoredFileInfo(
                    new StorageKey(relative),
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            }

            await Task.Yield();
        }
    }

    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default) =>
        await LocalStoragePaths.ProbeAsync(_root, cancellationToken).ConfigureAwait(false) is null;

    private string IncomingDirectoryFor(StorageKey key) =>
        key.SegmentCount == 1
            // A single-segment key is a file at the root, so there is no directory to nest in.
            ? Path.Combine(_root, LocalStoragePaths.IncomingDirectoryName)
            : Path.Combine(PathForRelative(key.FirstSegment), LocalStoragePaths.IncomingDirectoryName);

    private string PathFor(StorageKey key) => PathForRelative(key.Value);

    private string PathForRelative(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));

        if (!LocalStoragePaths.IsSameOrUnder(full, _root) || string.Equals(full, _root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A storage key resolved outside the storage root. This is a bug: keys are validated before they get here.");
        }

        return full;
    }
}
