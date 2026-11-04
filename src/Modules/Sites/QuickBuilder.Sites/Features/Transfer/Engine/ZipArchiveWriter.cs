using System.IO.Compression;
using System.Text.Json;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Sites.Features.Transfer.Engine;

/// <summary>
/// Writes a site archive to a stream that cannot seek, such as the pipe an export streams into
/// storage (020 research ST-02, ST-16).
/// </summary>
/// <remarks>
/// Entries are written one after another, each hashed as it is written; the manifest, which
/// lists them all, is the last entry. Memory stays bounded whatever the archive's size. JSON
/// entries are deflated; media is stored as it is, because images are already compressed.
/// </remarks>
internal sealed class ZipArchiveWriter : IArchiveWriter, IAsyncDisposable
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly ZipArchive _zip;
    private readonly List<ArchiveEntryInfo> _entries = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private bool _completed;

    private ZipArchiveWriter(ZipArchive zip) => _zip = zip;

    /// <summary>The entries written so far, in order.</summary>
    public IReadOnlyList<ArchiveEntryInfo> Entries => _entries;

    public static async Task<ZipArchiveWriter> CreateAsync(Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken)
            .ConfigureAwait(false);
        return new ZipArchiveWriter(zip);
    }

    public async Task<long> WriteLinesAsync<T>(string part, string entry, IAsyncEnumerable<T> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!ArchiveFormat.IsSegment(part) || !ArchiveFormat.IsSegment(entry))
        {
            throw new ArgumentException($"'{part}/{entry}' is not a valid part entry name.");
        }

        long count = 0;
        await WriteEntryAsync(ArchiveFormat.LinesEntry(part, entry), CompressionLevel.Optimal, async stream =>
        {
            await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, row, ArchiveJson.Options, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
                count++;
            }
        }, cancellationToken).ConfigureAwait(false);

        return count;
    }

    public Task<ArchiveEntryInfo> WriteMediaAsync(Guid fileId, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        return WriteEntryAsync(
            ArchiveFormat.MediaEntry(fileId),
            CompressionLevel.NoCompression,
            stream => content.CopyToAsync(stream, cancellationToken),
            cancellationToken);
    }

    /// <summary>Writes <c>authors.jsonl</c>: the id and display name of every member the parts name.</summary>
    public Task WriteAuthorsAsync(IAsyncEnumerable<ArchiveAuthor> authors, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authors);
        return WriteEntryAsync(ArchiveFormat.AuthorsEntry, CompressionLevel.Optimal, async stream =>
        {
            await foreach (var author in authors.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, author, ArchiveJson.Options, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <summary>Writes <c>manifest.json</c> with every entry written before it, and ends the archive.</summary>
    public async Task CompleteAsync(Func<IReadOnlyList<ArchiveEntryInfo>, ArchiveManifest> manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var document = manifest([.. _entries]);

        var entry = _zip.CreateEntry(ArchiveFormat.ManifestEntry, CompressionLevel.Optimal);
        await using (var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, document, ArchiveJson.Options, cancellationToken).ConfigureAwait(false);
        }

        _completed = true;
        await _zip.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<ArchiveEntryInfo> WriteEntryAsync(
        string name,
        CompressionLevel compression,
        Func<Stream, Task> write,
        CancellationToken cancellationToken)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The archive is complete.");
        }

        if (!ArchiveFormat.IsAllowedName(name) || name == ArchiveFormat.ManifestEntry || !_names.Add(name))
        {
            throw new ArgumentException($"'{name}' cannot be written: it is not an allowed entry name or it was written already.");
        }

        var entry = _zip.CreateEntry(name, compression);
        await using var raw = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var hashing = new HashingWriteStream(raw);
        await write(hashing).ConfigureAwait(false);
        await hashing.FlushAsync(cancellationToken).ConfigureAwait(false);

        var info = new ArchiveEntryInfo(name, hashing.Written, hashing.Finish());
        _entries.Add(info);
        return info;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            // Abandoned (failure or cancel): end the ZIP so the output stream can close. Storage
            // keeps nothing of a write that does not complete, so no partial archive survives.
            _completed = true;
            await _zip.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>One line of <c>authors.jsonl</c> (contracts/site-archive.md §3).</summary>
internal sealed record ArchiveAuthor(Guid Id, string DisplayName);
