namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// The body of one upload as storage reads it: the bytes already read for type detection,
/// then the rest of the request, counted against the size limit (003 FR-008, research M-09).
/// </summary>
/// <remarks>
/// Throws <see cref="UploadTooLargeException"/> the moment the count passes the limit, so the
/// server stops receiving and the provider discards its partial write. A failure reading the
/// request (the client went away) surfaces as <see cref="UploadInterruptedException"/>, so the
/// handler can tell it apart from a storage failure.
/// </remarks>
internal sealed class UploadStream(ReadOnlyMemory<byte> head, Stream body, long limit) : Stream
{
    private int _headPosition;

    /// <summary>Bytes handed to the reader so far.</summary>
    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;

        if (_headPosition < head.Length)
        {
            read = Math.Min(buffer.Length, head.Length - _headPosition);
            head.Slice(_headPosition, read).CopyTo(buffer);
            _headPosition += read;
        }
        else
        {
            try
            {
                read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                // The server's own body limit, set by UploadSizeLimitAttribute, fired first.
                throw new UploadTooLargeException(limit, ex);
            }
            catch (Exception ex) when (ex is IOException or BadHttpRequestException && !cancellationToken.IsCancellationRequested)
            {
                throw new UploadInterruptedException(ex);
            }
        }

        BytesRead += read;
        if (BytesRead > limit)
        {
            throw new UploadTooLargeException(limit);
        }

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Uploads are read asynchronously.");

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>The upload passed <c>Media:MaxFileSizeBytes</c>.</summary>
internal sealed class UploadTooLargeException : IOException
{
    public UploadTooLargeException()
    {
    }

    public UploadTooLargeException(string message) : base(message)
    {
    }

    public UploadTooLargeException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public UploadTooLargeException(long limit, Exception? innerException = null)
        : base($"The upload passed the limit of {limit} bytes.", innerException)
    {
        Limit = limit;
    }

    public long Limit { get; }
}

/// <summary>Reading the request body failed: the client went away or sent a malformed body.</summary>
internal sealed class UploadInterruptedException : IOException
{
    public UploadInterruptedException()
    {
    }

    public UploadInterruptedException(string message) : base(message)
    {
    }

    public UploadInterruptedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public UploadInterruptedException(Exception innerException)
        : base("The upload was interrupted before it finished.", innerException)
    {
    }
}
