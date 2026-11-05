using System.Security.Cryptography;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>Write-only stream that counts and hashes what passes through it to an inner stream.</summary>
internal sealed class HashingWriteStream(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    /// <summary>How many bytes have been written.</summary>
    public long Written { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => Written; set => throw new NotSupportedException(); }

    /// <summary>The SHA-256 of everything written, lower-case hex. Call once, at the end.</summary>
    public string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _hash.AppendData(buffer);
        Written += buffer.Length;
        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await HashChunks.AppendAsync(_hash, buffer, cancellationToken).ConfigureAwait(false);
        Written += buffer.Length;
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Hashes large buffers on <see cref="LowPriorityCpu"/>'s thread, so hashing a 2 GB archive does not
/// compete with visitors on the thread pool (020 SC-002); small ones, such as one JSON line, cost
/// less to hash than to hand over.
/// </summary>
internal static class HashChunks
{
    /// <summary>The smallest buffer handed to the low-priority thread.</summary>
    public const int Offloaded = 64 * 1024;

    public static ValueTask AppendAsync(IncrementalHash hash, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < Offloaded)
        {
            hash.AppendData(buffer.Span);
            return ValueTask.CompletedTask;
        }

        return new ValueTask(LowPriorityCpu.RunAsync(() => hash.AppendData(buffer.Span), cancellationToken));
    }
}

/// <summary>
/// Read-only stream over one archive entry that checks it against the manifest as it is read
/// (contracts/site-archive.md §2): it refuses to read past the declared length, which bounds
/// a compressed entry that expands further than it claims, and at the end compares the length
/// and SHA-256.
/// </summary>
internal sealed class VerifyingReadStream(Stream inner, ArchiveEntryInfo expected) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _read;
    private bool _verified;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => expected.Length;

    public override long Position { get => _read; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = inner.Read(Limit(buffer));
        return After(buffer[..count]);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = await inner.ReadAsync(Limit(buffer), cancellationToken).ConfigureAwait(false);
        if (count < HashChunks.Offloaded)
        {
            return After(buffer.Span[..count]);
        }

        // A large read, such as a media file's: hash it on the low-priority thread.
        _read += count;
        if (_read > expected.Length)
        {
            throw new ArchiveIntegrityException($"'{expected.Name}' is longer than the manifest says.");
        }

        await HashChunks.AppendAsync(_hash, buffer[..count], cancellationToken).ConfigureAwait(false);
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    // One byte more than declared is enough to know the entry is longer than it says.
    private Span<byte> Limit(Span<byte> buffer) => buffer[..(int)Math.Min(buffer.Length, expected.Length - _read + 1)];

    private Memory<byte> Limit(Memory<byte> buffer) => buffer[..(int)Math.Min(buffer.Length, expected.Length - _read + 1)];

    private int After(ReadOnlySpan<byte> data)
    {
        _read += data.Length;
        if (_read > expected.Length)
        {
            throw new ArchiveIntegrityException($"'{expected.Name}' is longer than the manifest says.");
        }

        _hash.AppendData(data);
        if (data.Length == 0 && !_verified)
        {
            _verified = true;
            if (_read != expected.Length)
            {
                throw new ArchiveIntegrityException($"'{expected.Name}' is shorter than the manifest says.");
            }

            if (!string.Equals(Convert.ToHexStringLower(_hash.GetHashAndReset()), expected.Sha256, StringComparison.Ordinal))
            {
                throw new ArchiveIntegrityException($"'{expected.Name}' does not match its hash in the manifest.");
            }
        }

        return data.Length;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _hash.Dispose();
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
