using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// Reads a site archive after checking its shape (020 contracts/site-archive.md §1–§2,
/// research ST-02).
/// </summary>
/// <remarks>
/// <see cref="OpenAsync"/> refuses, before reading any entry's content: a file that is not a
/// ZIP or has no central directory (truncated), any entry name outside the four allowed shapes,
/// directories, duplicates, encrypted entries, and an entry list that differs from the
/// manifest's. Every read afterwards checks the entry's length and SHA-256 as it goes.
/// <para>
/// A ZIP opened for reading shares one underlying stream, so it can read only one entry at a
/// time. The reader keeps two: one for <c>.jsonl</c> entries and one for media, because the media
/// part reads its file list while it copies bytes. Each is opened from the factory on first use.
/// </para>
/// </remarks>
internal sealed class ZipArchiveReader : IArchiveReader, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<Stream>> _open;
    private readonly ZipArchive _lines;
    private readonly Stream _linesStream;
    private readonly Dictionary<string, ArchiveEntryInfo> _expected;
    private ZipArchive? _media;
    private Stream? _mediaStream;

    private ZipArchiveReader(
        Func<CancellationToken, Task<Stream>> open,
        Stream linesStream,
        ZipArchive lines,
        ArchiveManifest manifest,
        Dictionary<string, ArchiveEntryInfo> expected)
    {
        _open = open;
        _linesStream = linesStream;
        _lines = lines;
        Manifest = manifest;
        _expected = expected;
    }

    public ArchiveManifest Manifest { get; }

    /// <summary>Opens and checks an archive. The factory must return a new, seekable stream on each call.</summary>
    public static async Task<Result<ZipArchiveReader>> OpenAsync(Func<CancellationToken, Task<Stream>> open, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);

        var stream = await open(cancellationToken).ConfigureAwait(false);
        if (!stream.CanSeek)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new ArgumentException("An archive must be read from a seekable stream.", nameof(open));
        }

        ZipArchive zip;
        try
        {
            zip = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return Result.Failure<ZipArchiveReader>(ArchiveErrors.Unreadable("The file is not a complete site archive."));
        }

        var checkedOpen = await CheckAsync(zip, cancellationToken).ConfigureAwait(false);
        if (checkedOpen.IsFailure)
        {
            await zip.DisposeAsync().ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
            return Result.Failure<ZipArchiveReader>(checkedOpen.Error!);
        }

        var (manifest, expected) = checkedOpen.Value;
        return Result.Success(new ZipArchiveReader(open, stream, zip, manifest, expected));
    }

    private static async Task<Result<(ArchiveManifest, Dictionary<string, ArchiveEntryInfo>)>> CheckAsync(
        ZipArchive zip,
        CancellationToken cancellationToken)
    {
        var present = new Dictionary<string, long>(StringComparer.Ordinal);
        ZipArchiveEntry? manifestEntry = null;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith('/') || !ArchiveFormat.IsAllowedName(name))
            {
                return Fail(ArchiveErrors.UnsafePath(name));
            }

            if (!present.TryAdd(name, entry.Length))
            {
                return Fail(ArchiveErrors.UnsafePath(name, duplicate: true));
            }

            if (entry.IsEncrypted)
            {
                return Fail(ArchiveErrors.Unreadable($"'{name}' is encrypted."));
            }

            if (name == ArchiveFormat.ManifestEntry)
            {
                manifestEntry = entry;
            }
        }

        if (manifestEntry is null)
        {
            return Fail(ArchiveErrors.Unreadable("The archive has no manifest.json."));
        }

        if (manifestEntry.Length > ArchiveFormat.MaxManifestBytes)
        {
            return Fail(ArchiveErrors.Unreadable("manifest.json is too large."));
        }

        ArchiveManifest? manifest;
        try
        {
            await using var stream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
            manifest = await JsonSerializer.DeserializeAsync<ArchiveManifest>(stream, ArchiveJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            return Fail(ArchiveErrors.Unreadable("manifest.json is not a valid site archive manifest."));
        }

        if (manifest is null || manifest.Entries is null || manifest.Parts is null || manifest.Source is null)
        {
            return Fail(ArchiveErrors.Unreadable("manifest.json is not a valid site archive manifest."));
        }

        var expected = new Dictionary<string, ArchiveEntryInfo>(StringComparer.Ordinal);
        foreach (var listed in manifest.Entries)
        {
            if (listed?.Name is not { } name || !ArchiveFormat.IsAllowedName(name) || name == ArchiveFormat.ManifestEntry)
            {
                return Fail(ArchiveErrors.UnsafePath(listed?.Name ?? "(none)"));
            }

            if (!expected.TryAdd(name, listed))
            {
                return Fail(ArchiveErrors.Integrity($"'{name}' is listed twice in the manifest."));
            }

            if (!present.TryGetValue(name, out var length))
            {
                return Fail(ArchiveErrors.Integrity($"'{name}' is listed in the manifest but missing from the archive."));
            }

            if (length != listed.Length)
            {
                return Fail(ArchiveErrors.Integrity($"'{name}' has a different length than the manifest says."));
            }
        }

        var extra = present.Keys.FirstOrDefault(name => name != ArchiveFormat.ManifestEntry && !expected.ContainsKey(name));
        if (extra is not null)
        {
            return Fail(ArchiveErrors.Integrity($"'{extra}' is in the archive but not listed in the manifest."));
        }

        return Result.Success((manifest, expected));

        static Result<(ArchiveManifest, Dictionary<string, ArchiveEntryInfo>)> Fail(Error error) =>
            Result.Failure<(ArchiveManifest, Dictionary<string, ArchiveEntryInfo>)>(error);
    }

    /// <summary>Reads every entry once to verify its hash, before any part reads it (research ST-24).</summary>
    public async Task<Result> VerifyAllAsync(Action<double>? progress, CancellationToken cancellationToken)
    {
        var total = Math.Max(1, _expected.Values.Sum(e => e.Length));
        long done = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            foreach (var expected in _expected.Values)
            {
                try
                {
                    await using var stream = await OpenVerifiedAsync(_lines, expected, cancellationToken).ConfigureAwait(false);
                    int read;
                    while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        done += read;
                        progress?.Invoke((double)done / total);
                    }
                }
                catch (ArchiveIntegrityException ex)
                {
                    return Result.Failure(ArchiveErrors.Integrity(ex.Message));
                }
                catch (InvalidDataException)
                {
                    return Result.Failure(ArchiveErrors.Unreadable($"'{expected.Name}' cannot be decompressed."));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Result.Success();
    }

    public bool HasLines(string part, string entry) => _expected.ContainsKey(ArchiveFormat.LinesEntry(part, entry));

    public bool HasMedia(Guid fileId) => _expected.ContainsKey(ArchiveFormat.MediaEntry(fileId));

    public ArchiveEntryInfo? MediaEntry(Guid fileId) => _expected.GetValueOrDefault(ArchiveFormat.MediaEntry(fileId));

    public IAsyncEnumerable<T> ReadLinesAsync<T>(string part, string entry, CancellationToken cancellationToken) =>
        ReadJsonLinesAsync<T>(ArchiveFormat.LinesEntry(part, entry), cancellationToken);

    /// <summary>Reads <c>authors.jsonl</c>.</summary>
    public IAsyncEnumerable<ArchiveAuthor> ReadAuthorsAsync(CancellationToken cancellationToken) =>
        ReadJsonLinesAsync<ArchiveAuthor>(ArchiveFormat.AuthorsEntry, cancellationToken);

    public async Task<Stream?> OpenMediaAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (_expected.GetValueOrDefault(ArchiveFormat.MediaEntry(fileId)) is not { } expected)
        {
            return null;
        }

        if (_media is null)
        {
            _mediaStream = await _open(cancellationToken).ConfigureAwait(false);
            _media = await ZipArchive.CreateAsync(_mediaStream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);
        }

        return await OpenVerifiedAsync(_media, expected, cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<T> ReadJsonLinesAsync<T>(string name, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_expected.GetValueOrDefault(name) is not { } expected)
        {
            yield break;
        }

        await using var stream = await OpenVerifiedAsync(_lines, expected, cancellationToken).ConfigureAwait(false);
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 64 * 1024, leaveOpen: true));
        var lineNumber = 0;
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = read.Buffer;

                while (TryReadLine(ref buffer, out var line))
                {
                    lineNumber++;
                    if (line.Length > 0)
                    {
                        yield return Deserialize<T>(line, name, lineNumber);
                    }
                }

                if (buffer.Length > ArchiveFormat.MaxLineBytes)
                {
                    throw new ArchiveFormatException($"Line {lineNumber + 1} of '{name}' is longer than {ArchiveFormat.MaxLineBytes} bytes.");
                }

                if (read.IsCompleted)
                {
                    if (buffer.Length > 0)
                    {
                        lineNumber++;
                        yield return Deserialize<T>(buffer, name, lineNumber);
                    }

                    break;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        if (buffer.PositionOf((byte)'\n') is not { } end)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, end);
        buffer = buffer.Slice(buffer.GetPosition(1, end));
        return true;
    }

    private static T Deserialize<T>(ReadOnlySequence<byte> line, string entry, int lineNumber)
    {
        try
        {
            var reader = new Utf8JsonReader(line);
            return JsonSerializer.Deserialize<T>(ref reader, ArchiveJson.Options)
                ?? throw new ArchiveFormatException($"Line {lineNumber} of '{entry}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new ArchiveFormatException($"Line {lineNumber} of '{entry}' cannot be read: {ex.Message}", ex);
        }
    }

    private static async Task<Stream> OpenVerifiedAsync(ZipArchive zip, ArchiveEntryInfo expected, CancellationToken cancellationToken)
    {
        var entry = zip.GetEntry(expected.Name)
            ?? throw new ArchiveIntegrityException($"'{expected.Name}' is missing from the archive.");
        var raw = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new VerifyingReadStream(raw, expected);
    }

    public async ValueTask DisposeAsync()
    {
        await _lines.DisposeAsync().ConfigureAwait(false);
        await _linesStream.DisposeAsync().ConfigureAwait(false);
        if (_media is not null)
        {
            await _media.DisposeAsync().ConfigureAwait(false);
        }

        if (_mediaStream is not null)
        {
            await _mediaStream.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>Why an archive cannot be read at all (data-model §5).</summary>
internal static class ArchiveErrors
{
    public static Error Unreadable(string message) => Error.Validation("archive.unreadable", message);

    public static Error Integrity(string message) => Error.Validation("archive.integrity", message);

    public static Error UnsafePath(string name, bool duplicate = false) => Error.Validation(
        "archive.unsafe_path",
        duplicate ? $"The archive holds '{Shorten(name)}' twice." : $"The archive holds an entry that is not allowed: '{Shorten(name)}'.");

    // An entry name comes from the file; keep what is echoed back short.
    private static string Shorten(string name) => name.Length <= 120 ? name : name[..120] + "…";
}
