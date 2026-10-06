namespace QuickBuilder.Shared.Infrastructure.Storage.S3;

/// <summary>Reads byte ranges of one stored object.</summary>
internal interface IRangeSource
{
    /// <summary>Fills <paramref name="destination"/> with the bytes starting at <paramref name="offset"/>.</summary>
    /// <remarks>The caller never asks past the end of the object.</remarks>
    Task ReadRangeAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken);
}

/// <summary>
/// A seekable, read-only view of a remote object that fetches ranges as they are needed
/// (013 contracts/storage-providers.md §3.3).
/// </summary>
/// <remarks>
/// <see cref="StoredFile.Content"/> must be seekable: that is how partial reads (HTTP range
/// requests) and the image reader work. A miss fetches a 1 MiB read-ahead; a read at least
/// that large goes straight to the caller's buffer.
/// </remarks>
internal sealed class S3RangeReadStream(IRangeSource source, long length, int readAhead = S3RangeReadStream.DefaultReadAhead) : Stream
{
    public const int DefaultReadAhead = 1024 * 1024;

    private byte[]? _buffer;
    private long _bufferStart;
    private int _bufferCount;
    private long _position;
    private bool _disposed;

    public override bool CanRead => !_disposed;

    public override bool CanSeek => !_disposed;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return length;
        }
    }

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));
        _position = target;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override int Read(Span<byte> buffer)
    {
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = ReadAsync(rented.AsMemory(0, buffer.Length)).AsTask().GetAwaiter().GetResult();
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var remaining = length - _position;
        if (remaining <= 0 || destination.Length == 0)
        {
            return 0;
        }

        if (!Buffered(_position))
        {
            if (destination.Length >= readAhead)
            {
                var direct = (int)Math.Min(destination.Length, remaining);
                await source.ReadRangeAsync(_position, destination[..direct], cancellationToken).ConfigureAwait(false);
                _position += direct;
                return direct;
            }

            _buffer ??= new byte[readAhead];
            _bufferCount = (int)Math.Min(readAhead, remaining);
            _bufferStart = _position;
            try
            {
                await source.ReadRangeAsync(_bufferStart, _buffer.AsMemory(0, _bufferCount), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _bufferCount = 0;
                throw;
            }
        }

        var from = (int)(_position - _bufferStart);
        var count = Math.Min(destination.Length, _bufferCount - from);
        _buffer.AsMemory(from, count).CopyTo(destination);
        _position += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        _buffer = null;
        base.Dispose(disposing);
    }

    private bool Buffered(long position) =>
        _buffer is not null && position >= _bufferStart && position < _bufferStart + _bufferCount;
}
