namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>Where a part writes its entries during an export (contracts/site-archive.md §1).</summary>
/// <remarks>
/// Names are checked against the archive's entry grammar, so a part cannot write an entry that
/// an import would refuse. Every entry is hashed as it is written and listed in the manifest.
/// </remarks>
public interface IArchiveWriter
{
    /// <summary>
    /// Writes <c>parts/&lt;part&gt;/&lt;entry&gt;.jsonl</c>, one JSON object per row, and returns how
    /// many rows it wrote. Rows are serialised with <see cref="ArchiveJson.Options"/>.
    /// </summary>
    Task<long> WriteLinesAsync<T>(string part, string entry, IAsyncEnumerable<T> rows, CancellationToken cancellationToken);

    /// <summary>Writes the bytes of one media file as <c>media/&lt;fileId&gt;</c> and returns its length and hash.</summary>
    Task<ArchiveEntryInfo> WriteMediaAsync(Guid fileId, Stream content, CancellationToken cancellationToken);
}

/// <summary>How a part reads an archive during a check or an import.</summary>
/// <remarks>
/// The reader has already checked the entry list against the manifest and the names against the
/// grammar. Every read verifies the entry's length and SHA-256 as it goes and throws
/// <see cref="ArchiveIntegrityException"/> on a mismatch.
/// </remarks>
public interface IArchiveReader
{
    ArchiveManifest Manifest { get; }

    /// <summary>Whether <c>parts/&lt;part&gt;/&lt;entry&gt;.jsonl</c> exists.</summary>
    bool HasLines(string part, string entry);

    /// <summary>
    /// Reads <c>parts/&lt;part&gt;/&lt;entry&gt;.jsonl</c> line by line. An absent entry reads as empty.
    /// A line that is not valid JSON for <typeparamref name="T"/> throws <see cref="ArchiveFormatException"/>
    /// naming the entry and line.
    /// </summary>
    IAsyncEnumerable<T> ReadLinesAsync<T>(string part, string entry, CancellationToken cancellationToken);

    /// <summary>Whether <c>media/&lt;fileId&gt;</c> exists.</summary>
    bool HasMedia(Guid fileId);

    /// <summary>The manifest's entry for <c>media/&lt;fileId&gt;</c>, or null.</summary>
    ArchiveEntryInfo? MediaEntry(Guid fileId);

    /// <summary>Opens the bytes of one media file, verified as they are read; null when absent.</summary>
    Task<Stream?> OpenMediaAsync(Guid fileId, CancellationToken cancellationToken);
}

/// <summary>An entry's bytes do not match the manifest (<c>archive.integrity</c>).</summary>
public sealed class ArchiveIntegrityException : Exception
{
    public ArchiveIntegrityException()
    {
    }

    public ArchiveIntegrityException(string message) : base(message)
    {
    }

    public ArchiveIntegrityException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>An entry's content cannot be read as its part declares (<c>transfer.document_invalid</c>).</summary>
public sealed class ArchiveFormatException : Exception
{
    public ArchiveFormatException()
    {
    }

    public ArchiveFormatException(string message) : base(message)
    {
    }

    public ArchiveFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>Progress of the running step, written to the job at most once a second.</summary>
public interface ITransferProgress
{
    /// <summary>Reports how far the current part is, from 0 to 1.</summary>
    void Report(double fraction);
}
