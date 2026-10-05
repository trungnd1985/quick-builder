using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Loads a site's folders, applies one structural change and saves it with the tree's row
/// version (013 research MV-02, data-model.md §3.2).
/// </summary>
/// <remarks>
/// Every structural change bumps <see cref="FolderTree.Revision"/> in the same save, so two
/// concurrent changes cannot both commit: the second fails with <c>media.folder_conflict</c>
/// and the member reloads. The database has the last word on the rest: the sibling-name
/// index answers <c>media.folder_name_taken</c>, and the <c>RESTRICT</c> foreign keys answer
/// <c>media.folder_not_empty</c> even when a file is moved in during a delete.
/// </remarks>
internal sealed class FolderWrites(MediaDbContext database)
{
    private FolderTree? _tree;

    /// <summary>Every folder of <paramref name="siteId"/>, tracked.</summary>
    /// <remarks>
    /// The tree row is read first and kept: its row version is what makes a change validated
    /// against these folders fail if another change committed meanwhile. Read at save time
    /// instead, it would already include the other change, and both would commit.
    /// </remarks>
    public async Task<FolderStructure> LoadAsync(Guid siteId, CancellationToken cancellationToken)
    {
        _tree = await FindTreeAsync(siteId, cancellationToken).ConfigureAwait(false);

        var folders = await database.Folders
            .AcrossAllSites("Admin folder change on a site named explicitly in the route (001 FR-011b).")
            .Where(f => f.SiteId == siteId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new FolderStructure(folders);
    }

    /// <summary>Adds a newly created folder to the save.</summary>
    public void Track(MediaFolder folder) => database.Folders.Add(folder);

    /// <summary>
    /// Bumps the site's tree revision and saves everything tracked, mapping the database's
    /// refusals to folder errors.
    /// </summary>
    public async Task<Result> SaveAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var tree = _tree ?? await FindTreeAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (tree is null)
        {
            tree = new FolderTree(siteId);
            database.FolderTrees.Add(tree);
        }

        tree.Touch();

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(MediaErrors.FolderConflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_folders_sibling_name",
        })
        {
            return Result.Failure(MediaErrors.FolderNameTaken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "pk_folder_trees",
        })
        {
            // Two first changes on a site raced to create its tree row.
            return Result.Failure(MediaErrors.FolderConflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: "fk_folders_parent" or "fk_media_files_folder",
        })
        {
            return Result.Failure(MediaErrors.FolderNotEmpty);
        }
    }

    private Task<FolderTree?> FindTreeAsync(Guid siteId, CancellationToken cancellationToken) =>
        database.FolderTrees
            .AcrossAllSites("Admin folder change on a site named explicitly in the route (001 FR-011b).")
            .FirstOrDefaultAsync(t => t.SiteId == siteId, cancellationToken);

    /// <summary>How many files each folder holds directly, and how many sit at the top level.</summary>
    public async Task<(Dictionary<Guid, int> ByFolder, int TopLevel)> FileCountsAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var counts = await database.MediaFiles
            .AcrossAllSites("Admin folder tree of a site named explicitly in the route (001 FR-011b).")
            .AsNoTracking()
            .Where(f => f.SiteId == siteId)
            .GroupBy(f => f.FolderId)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (
            counts.Where(c => c.FolderId is not null).ToDictionary(c => c.FolderId!.Value, c => c.Count),
            counts.FirstOrDefault(c => c.FolderId is null)?.Count ?? 0);
    }

    public static MediaFolderDto ToDto(MediaFolder folder, int fileCount)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new MediaFolderDto(folder.Id, folder.Name, folder.ParentId, fileCount, folder.CreatedAtUtc);
    }
}
