using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Media.Features.Transfer;

/// <summary>
/// The Media module's share of an archive: folders, file records and the bytes of each original
/// (020 contracts/archive-parts.md §3; FR-001, FR-009).
/// </summary>
/// <remarks>
/// Variants are not carried; they are rebuilt (research ST-03). A file's bytes are read by its
/// content-version key, which is never rewritten, so they belong to the snapshot's row. A key
/// that is gone is recorded as missing and does not stop the export (FR-009).
/// </remarks>
internal sealed partial class MediaArchivePart(MediaDbContext database, IFileStorage storage, Microsoft.Extensions.Options.IOptions<MediaOptions> options) : ISiteArchivePart
{
    public const string PartName = "media";

    public string Name => PartName;

    public int Order => 20;

    public int Version => 1;

    public IReadOnlyDictionary<string, int> Contracts { get; } = new Dictionary<string, int>();

    public async Task ExportAsync(PartExportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);

        var folders = await context.Archive.WriteLinesAsync(PartName, "folders", Folders(context, cancellationToken), cancellationToken).ConfigureAwait(false);
        context.Count(ArchiveKinds.Folder, folders);

        var total = await database.MediaFiles.CountAsync(cancellationToken).ConfigureAwait(false);
        var files = new List<MediaFile>(capacity: Math.Min(total, 10_000));
        long written = 0;
        long bytes = 0;

        // Bytes first, so each file's row can say whether its bytes are in the archive and give
        // their hash. Rows are listed by id, the order of their entries.
        var included = new Dictionary<Guid, string>();
        await foreach (var file in database.MediaFiles.AsNoTracking().OrderBy(f => f.Id).AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            context.Authors.Add(file.UploadedBy);
            context.Authors.Add(file.ReplacedBy);

            var stored = await storage.OpenReadAsync(new StorageKey(file.StorageKey), cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                context.Problem("transfer.media_missing", ArchiveKinds.MediaFile, file.Id);
            }
            else
            {
                await using (stored)
                {
                    var entry = await context.Archive.WriteMediaAsync(file.Id, stored.Content, cancellationToken).ConfigureAwait(false);
                    included[file.Id] = entry.Sha256;
                    bytes += entry.Length;
                }
            }

            files.Add(file);
            written++;
            context.Progress.Report(total == 0 ? 1 : (double)written / total);
        }

        await context.Archive.WriteLinesAsync(PartName, "files", Rows(files, included), cancellationToken).ConfigureAwait(false);
        context.Count(ArchiveKinds.MediaFile, files.Count);
        context.Count("media_bytes", bytes);
    }

    private async IAsyncEnumerable<MediaFolderRow> Folders(PartExportContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var folder in database.Folders.AsNoTracking().OrderBy(f => f.CreatedAtUtc).ThenBy(f => f.Id).AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            context.Authors.Add(folder.CreatedBy);
            yield return new MediaFolderRow(folder.Id, folder.ParentId, folder.Name, folder.CreatedAtUtc, folder.CreatedBy);
        }
    }

    private static async IAsyncEnumerable<MediaFileRow> Rows(IEnumerable<MediaFile> files, IReadOnlyDictionary<Guid, string> included)
    {
        foreach (var f in files)
        {
            var hash = included.GetValueOrDefault(f.Id);
            yield return new MediaFileRow(
                f.Id,
                f.FolderId,
                f.FileName,
                f.ContentType,
                f.Extension,
                f.SizeBytes,
                hash,
                f.AltText,
                f.Width,
                f.Height,
                f.IsAnimated,
                f.ContentVersion,
                f.UploadedAtUtc,
                f.UploadedBy,
                f.ReplacedAtUtc,
                f.ReplacedBy,
                hash is null ? MediaFileRow.Missing : MediaFileRow.Included);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// The SHA-256 of a stored original, lower-case hex, as the archive writes it. It is hashed on
    /// the low-priority thread, so visitors come first (020 SC-002).
    /// </summary>
    internal static async Task<string> HashAsync(Stream content, CancellationToken cancellationToken) =>
        Convert.ToHexStringLower(await LowPriorityCpu.Sha256Async(content, cancellationToken).ConfigureAwait(false));
}

/// <summary><c>parts/media/folders.jsonl</c>: one folder.</summary>
internal sealed record MediaFolderRow(Guid Id, Guid? ParentId, string Name, DateTimeOffset CreatedAtUtc, Guid CreatedBy);

/// <summary><c>parts/media/files.jsonl</c>: one file record.</summary>
internal sealed record MediaFileRow(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string ContentType,
    string Extension,
    long SizeBytes,
    string? Sha256,
    string? AltText,
    int? Width,
    int? Height,
    bool IsAnimated,
    int ContentVersion,
    DateTimeOffset UploadedAtUtc,
    Guid UploadedBy,
    DateTimeOffset? ReplacedAtUtc,
    Guid? ReplacedBy,
    string Bytes)
{
    public const string Included = "included";
    public const string Missing = "missing";
}
