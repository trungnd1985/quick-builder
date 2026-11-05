using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Sites.Contracts.Transfer;

namespace QuickBuilder.Media.Features.Transfer;

/// <summary>Checking and applying the Media part (020 FR-015, FR-019; research ST-05, ST-08).</summary>
/// <remarks>
/// A merge never conflicts on media: a file identical to one of the target's is reused, any
/// other is added (research ST-10). A folder in the same place with the same name is reused, so
/// the archive's files land beside the target's.
/// </remarks>
internal sealed partial class MediaArchivePart
{
    private const int Batch = 500;

    public async Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var merge = context.Mode == ImportMode.Merge;
        if (context.Session is { } session)
        {
            database.JoinTransfer(session.Connection, session.Transaction);
        }

        var folders = await Lines<MediaFolderRow>(context.Archive, "folders", cancellationToken).ConfigureAwait(false);
        foreach (var folder in folders)
        {
            context.Ids.Declare(ArchiveKinds.Folder, folder.Id, folder.Name);
        }

        ILookup<long, TargetFile>? targetFiles = null;
        if (merge)
        {
            await MatchFoldersAsync(context, folders, cancellationToken).ConfigureAwait(false);
            targetFiles = await TargetFilesAsync(cancellationToken).ConfigureAwait(false);
        }

        long files = 0;
        await foreach (var file in context.Archive.ReadLinesAsync<MediaFileRow>(PartName, "files", cancellationToken).ConfigureAwait(false))
        {
            context.Ids.Declare(ArchiveKinds.MediaFile, file.Id, file.FileName);
            files++;

            var subject = new TransferSubject(ArchiveKinds.MediaFile, file.Id, file.FileName);
            if (file.Bytes == MediaFileRow.Missing || !context.Archive.HasMedia(file.Id))
            {
                context.Report.Warning("transfer.media_missing", subject, "The archive holds its details but not its content.");
            }
            else
            {
                await using var content = await context.Archive.OpenMediaAsync(file.Id, cancellationToken).ConfigureAwait(false);
                var head = new byte[FileTypeDetector.HeadLength];
                var length = await content!.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (Refusal(file, head.AsSpan(0, length), context.Archive.MediaEntry(file.Id)!.Length) is { } reason)
                {
                    context.Report.Warning("transfer.media_refused", subject, reason + " It will not be imported.");
                }
                else if (targetFiles is not null && await IdenticalAsync(context, file, targetFiles, cancellationToken).ConfigureAwait(false) is { } same)
                {
                    // Research ST-08: an identical file is reused, never a conflict.
                    context.Ids.Reuse(ArchiveKinds.MediaFile, file.Id, same);
                }
            }
        }

        context.Report.Count(ArchiveKinds.Folder, folders.Count);
        context.Report.Count(ArchiveKinds.MediaFile, files);

        if (context.Mode == ImportMode.Restore)
        {
            // FR-027b: what a restore removes and brings back.
            var present = await database.MediaFiles.AsNoTracking().Select(f => new { f.Id, f.FileName }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var file in present.Where(f => !context.Ids.IsDeclared(ArchiveKinds.MediaFile, f.Id)))
            {
                context.Report.Removal(new TransferSubject(ArchiveKinds.MediaFile, file.Id, file.FileName));
                context.Ids.Note($"remove:media_file:{file.Id:N}");
            }
        }
    }

    public async Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);
        var site = context.TargetSiteId;
        var restore = context.Mode == ImportMode.Restore;
        var merge = context.Mode == ImportMode.Merge;
        if (restore)
        {
            // Research ST-12: the site's media rows go; the archive's come back under their ids.
            // Bytes no row names any more are removed by the orphan sweep.
            context.Report.Removed(ArchiveKinds.MediaFile, await database.MediaFiles.CountAsync(cancellationToken).ConfigureAwait(false));
            await database.VariantClaims.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.MediaFiles.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.Folders.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await database.FolderTrees.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        // Folders, parents first, through the same structure rules as creating them by hand. A
        // merge adds beside the target's folders and skips the ones it reuses.
        var existing = merge ? await database.Folders.ToListAsync(cancellationToken).ConfigureAwait(false) : [];
        var structure = new FolderStructure(existing);
        var pending = (await Lines<MediaFolderRow>(context.Archive, "folders", cancellationToken).ConfigureAwait(false))
            .Where(f => context.Ids.Outcome(ArchiveKinds.Folder, f.Id) != IdOutcome.Reused)
            .ToList();
        while (pending.Count > 0)
        {
            var ready = pending.Where(f => f.ParentId is null || structure.Find(context.Ids.Remap(ArchiveKinds.Folder, f.ParentId.Value)) is not null).ToList();
            if (ready.Count == 0)
            {
                throw new ArchiveFormatException("A media folder's parent is not in the archive.");
            }

            foreach (var row in ready)
            {
                var created = structure.Create(
                    context.Ids.Remap(ArchiveKinds.Folder, row.Id),
                    site,
                    row.ParentId is { } parent ? context.Ids.Remap(ArchiveKinds.Folder, parent) : null,
                    row.Name,
                    row.CreatedAtUtc,
                    context.Authors.Map(row.CreatedBy));
                if (created.IsFailure)
                {
                    throw new ArchiveFormatException($"The media folder '{row.Name}' cannot be created: {created.Error!.Message}");
                }

                database.Folders.Add(created.Value);
                pending.Remove(row);
            }
        }

        var added = structure.Count - existing.Count;
        if (added > 0)
        {
            var tree = merge ? await database.FolderTrees.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) : null;
            if (tree is null)
            {
                tree = new FolderTree(site);
                database.FolderTrees.Add(tree);
            }

            tree.Touch();
        }

        context.Report.Imported(ArchiveKinds.Folder, added);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        long imported = 0;
        await foreach (var row in context.Archive.ReadLinesAsync<MediaFileRow>(PartName, "files", cancellationToken).ConfigureAwait(false))
        {
            if (context.Ids.Outcome(ArchiveKinds.MediaFile, row.Id) == IdOutcome.Reused)
            {
                context.Report.Resolved(
                    new TransferSubject(ArchiveKinds.MediaFile, row.Id, row.FileName), context.Ids.Resolve(ArchiveKinds.MediaFile, row.Id)!.Value, IdOutcome.Reused);
                continue;
            }

            var id = context.Ids.Remap(ArchiveKinds.MediaFile, row.Id);
            // A restore keeps each file's content version, so its key is the one its bytes had.
            var key = restore ? MediaStorageKeys.For(site, id, Math.Max(1, row.ContentVersion)) : MediaStorageKeys.For(site, id);
            if (MediaFileType.FromMime(row.ContentType) is not { } type)
            {
                // Refused at the check, and accepted by the Administrator (FR-015): not stored,
                // and references to it stay broken.
                context.Report.LeftOut(new TransferSubject(ArchiveKinds.MediaFile, row.Id, row.FileName), "media_refused");
                continue;
            }

            // Bytes first, under a key no committed row uses: a failed import leaves them
            // without a row, and the orphan sweep removes them (research ST-05).
            var hasBytes = false;
            if (row.Bytes == MediaFileRow.Included)
            {
                await using var content = await context.Archive.OpenMediaAsync(row.Id, cancellationToken).ConfigureAwait(false);
                if (content is not null)
                {
                    // The same rules as an upload, again, on the bytes actually written (FR-015).
                    var head = new byte[FileTypeDetector.HeadLength];
                    var length = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                    if (Refusal(row, head.AsSpan(0, length), context.Archive.MediaEntry(row.Id)!.Length) is not null)
                    {
                        context.Report.LeftOut(new TransferSubject(ArchiveKinds.MediaFile, row.Id, row.FileName), "media_refused");
                        continue;
                    }

                    if (!restore || !await storage.ExistsAsync(key, cancellationToken).ConfigureAwait(false))
                    {
                        await storage.WriteAsync(key, new UploadStream(head.AsMemory(0, length), content, long.MaxValue), cancellationToken).ConfigureAwait(false);
                    }

                    hasBytes = true;
                }
            }

            if (!hasBytes)
            {
                context.Report.LeftOut(new TransferSubject(ArchiveKinds.MediaFile, row.Id, row.FileName), "media_missing");
            }

            var file = MediaFile.Create(
                id,
                site,
                row.FileName,
                type,
                row.Extension,
                row.SizeBytes,
                row.UploadedAtUtc,
                context.Authors.Map(row.UploadedBy),
                key.Value,
                row.FolderId is { } folder ? context.Ids.Remap(ArchiveKinds.Folder, folder) : null,
                row.Width is { } width && row.Height is { } height ? new ImageMetadata(width, height, row.IsAnimated) : null);
            if (row.AltText is not null)
            {
                file.SetAltText(row.AltText);
            }

            if (restore)
            {
                file.RestoreContentVersion(Math.Max(1, row.ContentVersion), row.ReplacedAtUtc, context.Authors.Map(row.ReplacedBy));
            }

            database.MediaFiles.Add(file);
            imported++;
            if (imported % Batch == 0)
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
        context.Report.Imported(ArchiveKinds.MediaFile, imported);
    }

    /// <summary>
    /// Why an archived file's bytes would be refused as an upload (Principle IX; FR-015): its
    /// content is not an allowed type, is not the type its record says, or is too large. Null
    /// when it would be accepted.
    /// </summary>
    private string? Refusal(MediaFileRow row, ReadOnlySpan<byte> head, long length)
    {
        var settings = options.Value;
        if (length > settings.MaxFileSizeBytes)
        {
            return "It is larger than this installation accepts.";
        }

        var detected = FileTypeDetector.Detect(head);
        if (detected is null || !settings.AllowedFileTypes.Contains(detected))
        {
            return "Its content is not a type this installation accepts.";
        }

        return detected.Mime == row.ContentType && detected.Accepts(row.Extension, row.ContentType)
            ? null
            : "Its content does not match its type.";
    }

    /// <summary>The target's files by size, for finding identical ones (research ST-08).</summary>
    private async Task<ILookup<long, TargetFile>> TargetFilesAsync(CancellationToken cancellationToken) =>
        (await database.MediaFiles.AsNoTracking()
            .Select(f => new TargetFile(f.Id, f.SizeBytes, f.StorageKey))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
        .ToLookup(f => f.SizeBytes);

    /// <summary>
    /// A target file with the same content as the archive's, or null. Only files of the same size
    /// are hashed, so most target files are never read (research ST-08 note). From the same site,
    /// the file with the same id is tried first (FR-022a).
    /// </summary>
    private async Task<Guid?> IdenticalAsync(PartCheckContext context, MediaFileRow row, ILookup<long, TargetFile> targets, CancellationToken cancellationToken)
    {
        if (context.Archive.MediaEntry(row.Id) is not { } entry)
        {
            return null;
        }

        foreach (var candidate in targets[entry.Length].OrderBy(f => context.SameSiteAsTarget && f.Id == row.Id ? 0 : 1))
        {
            if (candidate.Hash is null)
            {
                await using var stored = await storage.OpenReadAsync(new StorageKey(candidate.StorageKey), cancellationToken).ConfigureAwait(false);
                if (stored is null)
                {
                    continue;
                }

                candidate.Hash = await HashAsync(stored.Content, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(candidate.Hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Reuses each target folder in the same place with the same name. Folders are containers,
    /// never conflicts: what they hold is matched on its own.
    /// </summary>
    private async Task MatchFoldersAsync(PartCheckContext context, List<MediaFolderRow> folders, CancellationToken cancellationToken)
    {
        var target = await database.Folders.AsNoTracking()
            .Select(f => new { f.Id, f.ParentId, f.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Parents first: a folder is reused only where its parent was.
        var placed = new Dictionary<Guid, Guid?>();
        var pending = folders.ToList();
        while (pending.Count > 0)
        {
            var ready = pending.Where(f => f.ParentId is null || placed.ContainsKey(f.ParentId.Value) || pending.All(p => p.Id != f.ParentId)).ToList();
            if (ready.Count == 0)
            {
                return;
            }

            foreach (var row in ready)
            {
                pending.Remove(row);
                Guid? parent = row.ParentId is { } archiveParent ? placed.GetValueOrDefault(archiveParent) : null;
                if (row.ParentId is not null && parent is null)
                {
                    // Its parent is new, so it is too.
                    placed[row.Id] = null;
                    continue;
                }

                var match = target.FirstOrDefault(f => f.ParentId == parent && string.Equals(f.Name, row.Name, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    context.Ids.Reuse(ArchiveKinds.Folder, row.Id, match.Id);
                }

                placed[row.Id] = match?.Id;
            }
        }
    }

    private static async Task<List<T>> Lines<T>(IArchiveReader archive, string entry, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await foreach (var row in archive.ReadLinesAsync<T>(PartName, entry, cancellationToken).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        return rows;
    }

    private sealed record TargetFile(Guid Id, long SizeBytes, string StorageKey)
    {
        public string? Hash { get; set; }
    }
}
