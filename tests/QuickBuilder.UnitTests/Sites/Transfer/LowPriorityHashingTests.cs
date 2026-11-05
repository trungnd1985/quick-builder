using System.Security.Cryptography;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Features.Transfer.Engine;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>
/// Archive hashing on the low-priority thread gives the same hashes and still refuses a changed
/// entry (020 SC-002, contracts/site-archive.md §2).
/// </summary>
public sealed class LowPriorityHashingTests
{
    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(20).NextBytes(bytes);
        return bytes;
    }

    [Fact]
    public async Task Work_runs_on_the_dedicated_thread_not_the_pool()
    {
        string? name = null;
        var pooled = true;
        await LowPriorityCpu.RunAsync(() =>
        {
            name = Thread.CurrentThread.Name;
            pooled = Thread.CurrentThread.IsThreadPoolThread;
        }, CancellationToken.None);

        Assert.Equal("QuickBuilder low-priority CPU", name);
        Assert.False(pooled);
    }

    [Fact]
    public async Task A_stream_hashes_as_SHA256_does()
    {
        var bytes = Bytes((3 * 1024 * 1024) + 17);

        var hash = await LowPriorityCpu.Sha256Async(new MemoryStream(bytes), CancellationToken.None);

        Assert.Equal(SHA256.HashData(bytes), hash);
    }

    [Fact]
    public async Task Large_and_small_writes_hash_as_one_entry()
    {
        var bytes = Bytes((HashChunks.Offloaded * 3) + 5);
        await using var hashing = new HashingWriteStream(Stream.Null);

        await hashing.WriteAsync(bytes.AsMemory(0, 10));
        await hashing.WriteAsync(bytes.AsMemory(10, HashChunks.Offloaded * 2));
        await hashing.WriteAsync(bytes.AsMemory(10 + (HashChunks.Offloaded * 2)));

        Assert.Equal(bytes.LongLength, hashing.Written);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), hashing.Finish());
    }

    [Fact]
    public async Task Large_reads_still_verify_the_entry()
    {
        var bytes = Bytes((HashChunks.Offloaded * 4) + 3);
        var expected = new ArchiveEntryInfo("media/x", bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        await using (var good = new VerifyingReadStream(new MemoryStream(bytes), expected))
        {
            await good.CopyToAsync(Stream.Null, HashChunks.Offloaded * 2);
        }

        var changed = (byte[])bytes.Clone();
        changed[HashChunks.Offloaded + 1] ^= 0xFF;
        await using var bad = new VerifyingReadStream(new MemoryStream(changed), expected);
        await Assert.ThrowsAsync<ArchiveIntegrityException>(() => bad.CopyToAsync(Stream.Null, HashChunks.Offloaded * 2));

        var longer = bytes.Concat(new byte[HashChunks.Offloaded]).ToArray();
        await using var tooLong = new VerifyingReadStream(new MemoryStream(longer), expected);
        await Assert.ThrowsAsync<ArchiveIntegrityException>(() => tooLong.CopyToAsync(Stream.Null, HashChunks.Offloaded * 2));
    }
}
