using QuickBuilder.Shared.Infrastructure.Storage.S3;

namespace QuickBuilder.UnitTests.Storage;

/// <summary>The seekable remote stream behind S3 reads (013 tasks T074, contracts/storage-providers.md §3.3).</summary>
public sealed class S3RangeReadStreamTests
{
    private const int ReadAhead = 100;

    private static readonly byte[] Content = [.. Enumerable.Range(0, 1_000).Select(i => (byte)(i % 251))];

    [Fact]
    public async Task Sequential_reads_cross_buffer_boundaries_and_fetch_each_range_once()
    {
        var source = new FakeSource(Content);
        using var stream = new S3RangeReadStream(source, Content.Length, ReadAhead);

        using var copy = new MemoryStream();
        var buffer = new byte[30];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            copy.Write(buffer, 0, read);
        }

        Assert.Equal(Content, copy.ToArray());
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (i * 100L, 100)), source.Requests);
    }

    [Fact]
    public async Task Seeking_fetches_only_the_ranges_needed()
    {
        var source = new FakeSource(Content);
        using var stream = new S3RangeReadStream(source, Content.Length, ReadAhead);
        var buffer = new byte[10];

        stream.Seek(500, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer);
        Assert.Equal(Content.AsSpan(500, 10).ToArray(), buffer);

        // Inside the range already fetched: no request.
        stream.Seek(550, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer);

        // Backwards, then from the end.
        stream.Seek(20, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer);
        stream.Seek(-5, SeekOrigin.End);
        Assert.Equal(5, await stream.ReadAsync(buffer));
        Assert.Equal(Content.AsSpan(995, 5).ToArray(), buffer[..5]);

        Assert.Equal([(500L, 100), (20L, 100), (995L, 5)], source.Requests);
    }

    [Fact]
    public async Task A_read_larger_than_the_read_ahead_goes_straight_to_the_caller()
    {
        var source = new FakeSource(Content);
        using var stream = new S3RangeReadStream(source, Content.Length, ReadAhead);
        var buffer = new byte[400];

        Assert.Equal(400, await stream.ReadAsync(buffer));

        Assert.Equal([(0L, 400)], source.Requests);
        Assert.Equal(Content.AsSpan(0, 400).ToArray(), buffer);
    }

    [Fact]
    public async Task Length_position_and_the_end()
    {
        var source = new FakeSource(Content);
        using var stream = new S3RangeReadStream(source, Content.Length, ReadAhead);

        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Equal(1_000, stream.Length);

        stream.Position = 1_000;
        Assert.Equal(0, await stream.ReadAsync(new byte[10]));
        stream.Position = 5_000;
        Assert.Equal(0, await stream.ReadAsync(new byte[10]));
        Assert.Empty(source.Requests);

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin));
    }

    [Fact]
    public void Synchronous_reads_work_too()
    {
        using var stream = new S3RangeReadStream(new FakeSource(Content), Content.Length, ReadAhead);
        var buffer = new byte[50];

        Assert.Equal(50, stream.Read(buffer, 0, 50));
        Assert.Equal(50, stream.Read(buffer.AsSpan()));
        Assert.Equal(Content.AsSpan(50, 50).ToArray(), buffer);
    }

    [Fact]
    public async Task A_disposed_stream_refuses_reads()
    {
        var stream = new S3RangeReadStream(new FakeSource(Content), Content.Length, ReadAhead);
        await stream.DisposeAsync();

        Assert.False(stream.CanRead);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync(new byte[1]).AsTask());
    }

    private sealed class FakeSource(byte[] content) : IRangeSource
    {
        public List<(long Offset, int Count)> Requests { get; } = [];

        public Task ReadRangeAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
        {
            Requests.Add((offset, destination.Length));
            content.AsMemory((int)offset, destination.Length).CopyTo(destination);
            return Task.CompletedTask;
        }
    }
}
