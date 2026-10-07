using System.Security.Cryptography;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// What every storage provider must do (003 contracts/storage-provider.md §5, FR-024).
/// </summary>
/// <remarks>
/// Written against <see cref="IFileStorage"/>, not against the local provider, so it is the
/// thing that stops the interface quietly acquiring file-system assumptions (research M-19).
/// A provider's test class derives from this and supplies a fresh, empty instance per test.
/// Adding a case here means every provider must pass it.
/// </remarks>
public abstract class FileStorageConformanceTests : IDisposable
{
    private static readonly StorageKey KeyA = new("site-a/media/file-1");

    private IFileStorage? _storage;

    /// <summary>A fresh, empty provider for this test.</summary>
    protected abstract IFileStorage CreateStorage();

    /// <summary>Releases whatever <see cref="CreateStorage"/> created.</summary>
    protected virtual void Cleanup()
    {
    }

    private IFileStorage Storage => _storage ??= CreateStorage();

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // --- Round trip ---------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024 * 1024)]
    [InlineData(25 * 1024 * 1024)]
    public async Task Written_bytes_read_back_identically(int size)
    {
        var bytes = RandomBytes(size);

        await Storage.WriteAsync(KeyA, new MemoryStream(bytes));

        await using var stored = await Storage.OpenReadAsync(KeyA);
        Assert.NotNull(stored);
        Assert.Equal(size, stored.Length);
        Assert.Equal(bytes, await ReadAllAsync(stored.Content));
    }

    [Fact]
    public async Task Last_modified_is_close_to_the_time_of_writing()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        await Storage.WriteAsync(KeyA, new MemoryStream([1, 2, 3]));

        await using var stored = await Storage.OpenReadAsync(KeyA);
        Assert.NotNull(stored);
        Assert.InRange(stored.LastModifiedUtc, before, DateTimeOffset.UtcNow.AddSeconds(5));
    }

    // --- Create-only --------------------------------------------------------------------

    [Fact]
    public async Task A_second_write_to_the_same_key_is_refused_and_the_original_survives()
    {
        await Storage.WriteAsync(KeyA, new MemoryStream([1, 2, 3]));

        await Assert.ThrowsAsync<StorageKeyExistsException>(
            () => Storage.WriteAsync(KeyA, new MemoryStream([9, 9, 9, 9])));

        await using var stored = await Storage.OpenReadAsync(KeyA);
        Assert.Equal([1, 2, 3], await ReadAllAsync(stored!.Content));
    }

    // --- Atomicity ----------------------------------------------------------------------

    [Fact]
    public async Task A_write_whose_input_fails_halfway_leaves_nothing_behind()
    {
        await Assert.ThrowsAsync<IOException>(
            () => Storage.WriteAsync(KeyA, new FailingStream(RandomBytes(256 * 1024), failAfter: 100_000)));

        await AssertAbsentAsync(KeyA);
    }

    [Fact]
    public async Task A_cancelled_write_leaves_nothing_behind()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new FailingStream(RandomBytes(256 * 1024), failAfter: 100_000, onFail: cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Storage.WriteAsync(KeyA, content, cancellation.Token));

        await AssertAbsentAsync(KeyA);
    }

    // --- Partial reads ------------------------------------------------------------------

    [Fact]
    public async Task Reads_after_a_seek_return_the_bytes_at_that_offset()
    {
        var bytes = RandomBytes(100_000);
        await Storage.WriteAsync(KeyA, new MemoryStream(bytes));

        await using var stored = await Storage.OpenReadAsync(KeyA);
        Assert.NotNull(stored);
        Assert.True(stored.Content.CanSeek, "Stored content must be seekable: that is how partial reads work.");

        foreach (var offset in new[] { 0, 50_000, 99_999 })
        {
            stored.Content.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[Math.Min(10, bytes.Length - offset)];
            var read = await stored.Content.ReadAtLeastAsync(buffer, buffer.Length);

            Assert.Equal(buffer.Length, read);
            Assert.Equal(bytes.AsSpan(offset, buffer.Length).ToArray(), buffer);
        }
    }

    // --- Missing keys and delete --------------------------------------------------------

    [Fact]
    public async Task A_missing_key_reads_as_null_does_not_exist_and_deletes_quietly()
    {
        var missing = new StorageKey("site-a/media/never-written");

        Assert.Null(await Storage.OpenReadAsync(missing));
        Assert.False(await Storage.ExistsAsync(missing));
        await Storage.DeleteAsync(missing);
    }

    [Fact]
    public async Task Delete_is_idempotent_and_every_operation_agrees_afterwards()
    {
        await Storage.WriteAsync(KeyA, new MemoryStream([1]));
        Assert.True(await Storage.ExistsAsync(KeyA));

        await Storage.DeleteAsync(KeyA);
        await Storage.DeleteAsync(KeyA);

        await AssertAbsentAsync(KeyA);
    }

    /// <summary>
    /// Idempotent under concurrency too: two sweeps on two instances may delete the same key
    /// at the same moment (003 research M-15). Found by the orphan sweep tests on Windows,
    /// where a second delete of a delete-pending file reports "access denied".
    /// </summary>
    [Fact]
    public async Task Concurrent_deletes_of_one_key_all_succeed()
    {
        for (var round = 0; round < 10; round++)
        {
            var key = new StorageKey($"site-a/media/race-{round}");
            await Storage.WriteAsync(key, new MemoryStream(RandomBytes(4096)));

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Storage.DeleteAsync(key))));

            await AssertAbsentAsync(key);
        }
    }

    // --- Listing ------------------------------------------------------------------------

    [Fact]
    public async Task Listing_returns_every_committed_key_under_the_prefix_exactly_once()
    {
        var keys = Enumerable.Range(0, 12).Select(i => new StorageKey($"site-a/media/file-{i}")).ToList();
        foreach (var key in keys)
        {
            await Storage.WriteAsync(key, new MemoryStream(new byte[key.Value.Length]));
        }

        await Storage.WriteAsync(new StorageKey("site-a/other/x"), new MemoryStream([1]));

        var listed = await ListAsync("site-a/media/");

        Assert.Equal(keys.Select(k => k.Value).Order(), listed.Select(e => e.Key.Value).Order());
        Assert.All(listed, e => Assert.Equal(e.Key.Value.Length, e.Length));
    }

    [Fact]
    public async Task Listing_never_includes_a_write_that_failed()
    {
        await Storage.WriteAsync(KeyA, new MemoryStream([1]));
        await Assert.ThrowsAsync<IOException>(() => Storage.WriteAsync(
            new StorageKey("site-a/media/broken"),
            new FailingStream(RandomBytes(64 * 1024), failAfter: 1_000)));

        var listed = await ListAsync(string.Empty);

        Assert.Equal([KeyA.Value], listed.Select(e => e.Key.Value));
    }

    [Fact]
    public async Task Listing_one_site_never_shows_another()
    {
        await Storage.WriteAsync(new StorageKey("site-a/media/one"), new MemoryStream([1]));
        await Storage.WriteAsync(new StorageKey("site-b/media/two"), new MemoryStream([2]));

        var siteA = await ListAsync("site-a/");
        var siteB = await ListAsync("site-b/");

        Assert.Equal(["site-a/media/one"], siteA.Select(e => e.Key.Value));
        Assert.Equal(["site-b/media/two"], siteB.Select(e => e.Key.Value));
    }

    [Fact]
    public async Task Listing_an_empty_prefix_returns_nothing_from_an_empty_store() =>
        Assert.Empty(await ListAsync(string.Empty));

    [Theory]
    [InlineData("../")]
    [InlineData("/")]
    [InlineData("Site/")]
    public async Task Listing_refuses_a_prefix_no_key_could_start_with(string prefix) =>
        await Assert.ThrowsAsync<ArgumentException>(() => ListAsync(prefix));

    // --- Grammar ------------------------------------------------------------------------

    [Theory]
    [InlineData("..")]
    [InlineData("/x")]
    [InlineData("x/")]
    [InlineData("UPPER")]
    [InlineData("%2e")]
    [InlineData("a\\b")]
    [InlineData("a//b")]
    [InlineData("a/b/c/d/e/f/g/h/i")]
    public void Keys_outside_the_grammar_never_reach_the_provider(string value) =>
        Assert.Throws<ArgumentException>(() => new StorageKey(value));

    // --- Concurrency --------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_writers_on_distinct_keys_all_succeed()
    {
        var writes = Enumerable.Range(0, 20).Select(i =>
            Storage.WriteAsync(new StorageKey($"site-a/media/concurrent-{i}"), new MemoryStream(RandomBytes(64 * 1024))));

        await Task.WhenAll(writes);

        Assert.Equal(20, (await ListAsync("site-a/media/")).Count);
    }

    [Fact]
    public async Task Concurrent_writers_on_one_key_yield_exactly_one_success()
    {
        var payloads = Enumerable.Range(0, 20).Select(i => RandomBytes(32 * 1024 + i)).ToList();

        // Each writer starts on the thread pool, so the writers really overlap. Started from one
        // thread, they seldom reached the publish step together, and a race in the local
        // provider (a rename replacing a file another writer had just published, on Linux)
        // failed only now and then in CI.
        // Created once, before the writers start: the lazy property is not thread-safe, and a
        // remote provider created per thread would give each writer its own key prefix.
        var storage = Storage;
        var outcomes = await Task.WhenAll(payloads.Select(payload => Task.Run(async () =>
        {
            try
            {
                await storage.WriteAsync(KeyA, new MemoryStream(payload));
                return true;
            }
            catch (StorageKeyExistsException)
            {
                return false;
            }
        })));

        Assert.Equal(1, outcomes.Count(ok => ok));

        await using var stored = await Storage.OpenReadAsync(KeyA);
        var winner = payloads[Array.IndexOf(outcomes, true)];
        Assert.Equal(winner, await ReadAllAsync(stored!.Content));
    }

    // --- Helpers ------------------------------------------------------------------------

    protected static byte[] RandomBytes(int size)
    {
        var bytes = new byte[size];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private async Task AssertAbsentAsync(StorageKey key)
    {
        Assert.False(await Storage.ExistsAsync(key));
        Assert.Null(await Storage.OpenReadAsync(key));
        Assert.DoesNotContain(await ListAsync(string.Empty), e => e.Key == key);
    }

    private async Task<List<StoredFileInfo>> ListAsync(string prefix)
    {
        var entries = new List<StoredFileInfo>();
        await foreach (var entry in Storage.ListAsync(prefix))
        {
            entries.Add(entry);
        }

        return entries;
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    /// <summary>A stream that throws (or runs a callback) part-way through its content.</summary>
    protected sealed class FailingStream(byte[] content, int failAfter, Action? onFail = null) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position >= failAfter)
            {
                if (onFail is null)
                {
                    throw new IOException("Simulated failure part-way through the upload.");
                }

                onFail();
            }

            var count = Math.Min(Math.Min(buffer.Length, content.Length - _position), Math.Max(1, failAfter - _position));
            content.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Read(buffer.Span);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(read);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
