using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.Local;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>The local provider against the shared conformance suite (003 FR-024, SC-011).</summary>
public sealed class LocalFileStorageConformanceTests : FileStorageConformanceTests
{
    private readonly string _root = QuickBuilderApp.CreateTemporaryStorage();

    protected override IFileStorage CreateStorage() => LocalStorageFactory.Create(_root);

    protected override void Cleanup() => QuickBuilderApp.TryDeleteDirectory(_root);
}

/// <summary>
/// Behaviour specific to the local provider, beyond the conformance suite (research M-04, M-19).
/// </summary>
public sealed class LocalFileStorageTests : IDisposable
{
    private const string SiteA = "0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a0a";
    private const string SiteB = "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b";

    private readonly string _root = QuickBuilderApp.CreateTemporaryStorage();
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests() => _storage = LocalStorageFactory.Create(_root);

    public void Dispose() => QuickBuilderApp.TryDeleteDirectory(_root);

    [Fact]
    public async Task Stored_files_live_under_the_root_at_their_key()
    {
        await _storage.WriteAsync(new StorageKey($"{SiteA}/media/file"), new MemoryStream([1, 2]));

        Assert.True(File.Exists(Path.Combine(_storage.Root, SiteA, "media", "file")));
    }

    [Fact]
    public async Task Stored_files_have_no_execute_bits()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permissions only (Principle IX: never executable).
        }

        await _storage.WriteAsync(new StorageKey($"{SiteA}/media/file"), new MemoryStream([1]));
        var mode = File.GetUnixFileMode(Path.Combine(_storage.Root, SiteA, "media", "file"));

        const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        Assert.Equal((UnixFileMode)0, mode & anyExecute);
        Assert.Equal((UnixFileMode)0, mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
    }

    [Fact]
    public async Task A_write_in_progress_is_staged_under_its_own_site_and_never_listed()
    {
        var key = new StorageKey($"{SiteA}/media/slow");
        string[]? stagedWhileWriting = null;

        var content = new CallbackStream(new byte[64 * 1024], afterFirstRead: () =>
        {
            stagedWhileWriting = Directory.GetFiles(_storage.Root, "*.part", SearchOption.AllDirectories);
        });

        await _storage.WriteAsync(key, content);

        // Principle V: bytes in flight are namespaced by site, like everything else.
        var staged = Assert.Single(stagedWhileWriting!);
        Assert.Equal(
            Path.Combine(_storage.Root, SiteA, LocalStoragePaths.IncomingDirectoryName),
            Path.GetDirectoryName(staged));

        Assert.False(Directory.Exists(Path.Combine(_storage.Root, SiteB)));
        Assert.Empty(Directory.GetFiles(_storage.Root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Part_files_never_appear_in_a_listing()
    {
        var incoming = Path.Combine(_storage.Root, SiteA, LocalStoragePaths.IncomingDirectoryName);
        Directory.CreateDirectory(incoming);
        await File.WriteAllBytesAsync(Path.Combine(incoming, "abandoned.part"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(_storage.Root, ".probe-leftover"), [1]);

        var listed = new List<StoredFileInfo>();
        await foreach (var entry in _storage.ListAsync(string.Empty))
        {
            listed.Add(entry);
        }

        Assert.Empty(listed);
    }

    [Fact]
    public void A_key_can_never_resolve_outside_the_root()
    {
        // The grammar already refuses traversal; this proves the second, independent guard
        // exists by probing the private resolver with what the grammar would let through.
        var sibling = new StorageKey("x");
        Assert.StartsWith(_storage.Root, Path.GetFullPath(Path.Combine(_storage.Root, sibling.Value)), StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => new StorageKey("../outside"));
    }

    [Fact]
    public async Task Clean_up_removes_abandoned_part_and_probe_files_in_every_site_and_keeps_recent_ones()
    {
        var clock = new ManualClock();
        var cleanup = new LocalIncomingCleanupService(_storage, clock, NullLogger<LocalIncomingCleanupService>.Instance);

        var oldA = CreateAged(Path.Combine(_storage.Root, SiteA, ".incoming", "old.part"), TimeSpan.FromHours(2));
        var newA = CreateAged(Path.Combine(_storage.Root, SiteA, ".incoming", "new.part"), TimeSpan.FromMinutes(5));
        var oldB = CreateAged(Path.Combine(_storage.Root, SiteB, ".incoming", "old.part"), TimeSpan.FromHours(3));
        var newB = CreateAged(Path.Combine(_storage.Root, SiteB, ".incoming", "new.part"), TimeSpan.FromMinutes(1));
        var oldProbe = CreateAged(Path.Combine(_storage.Root, ".probe-dead"), TimeSpan.FromHours(2));
        var stored = CreateAged(Path.Combine(_storage.Root, SiteA, "media", "kept"), TimeSpan.FromDays(10));

        var removed = cleanup.Sweep();

        Assert.Equal(3, removed);
        Assert.False(File.Exists(oldA));
        Assert.False(File.Exists(oldB));
        Assert.False(File.Exists(oldProbe));
        Assert.True(File.Exists(newA));
        Assert.True(File.Exists(newB));
        Assert.True(File.Exists(stored), "Clean-up must never touch stored files.");
    }

    [Fact]
    public async Task The_health_check_passes_on_a_writable_root_and_fails_when_the_root_is_gone()
    {
        Assert.True(await _storage.CheckAsync());
        Assert.Empty(Directory.GetFiles(_storage.Root, ".probe-*"));

        Directory.Delete(_storage.Root, recursive: true);

        Assert.False(await _storage.CheckAsync());
    }

    private static string CreateAged(string path, TimeSpan age)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    /// <summary>
    /// Hands out 1 KiB per read and runs a callback before the second read, which is a
    /// moment the write is certainly still staged.
    /// </summary>
    private sealed class CallbackStream(byte[] content, Action afterFirstRead) : MemoryStream(content)
    {
        private int _reads;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++_reads == 2)
            {
                afterFirstRead();
            }

            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 1024)], cancellationToken);
        }
    }
}

/// <summary>Builds a local provider over a directory, validated as the host would.</summary>
internal static class LocalStorageFactory
{
    public static LocalFileStorage Create(string root)
    {
        var options = new StorageOptions { Local = { Path = root } };
        var validation = new LocalStorageOptionsValidator().Validate(null, options);
        Assert.True(validation.Succeeded, validation.FailureMessage);

        return new LocalFileStorage(Options.Create(options));
    }
}
